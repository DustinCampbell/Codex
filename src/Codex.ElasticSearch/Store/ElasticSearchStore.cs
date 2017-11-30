﻿using Codex.Framework.Types;
using Codex.Sdk.Utilities;
using Codex.Storage.ElasticProviders;
using System;
using System.Text;
using System.Threading.Tasks;
using Codex.ObjectModel;
using Codex.Storage.DataModel;
using static Codex.Utilities.SerializationUtilities;
using Codex.Utilities;
using Codex.Analysis;
using System.Collections.Concurrent;
using Nest;

namespace Codex.ElasticSearch
{
    public abstract partial class ElasticSearchStoreBase
    {
        internal readonly ElasticSearchService Service;
        internal readonly ElasticSearchStoreConfiguration Configuration;

        /// <summary>
        /// Creates an elasticsearch store with the given prefix for indices
        /// </summary>
        public ElasticSearchStoreBase(ElasticSearchStoreConfiguration configuration, ElasticSearchService service)
        {
            Configuration = configuration;
            Service = service;
        }

        public abstract Task<ElasticSearchEntityStore<TSearchType>> CreateStoreAsync<TSearchType>(SearchType searchType)
            where TSearchType : class, ISearchEntity;
    }

    public class ElasticSearchStore : ElasticSearchStoreBase, ICodexStore
    {
        internal readonly ElasticSearchEntityStore[] EntityStores = new ElasticSearchEntityStore[SearchTypes.RegisteredSearchTypes.Count];
        public readonly string StoredFilterPipelineId;

        public ElasticSearchStore(ElasticSearchStoreConfiguration configuration, ElasticSearchService service)
            : base(configuration, service)
        {

            StoredFilterPipelineId = configuration.Prefix + "StoredFilterPipeline";
        }

        public async Task Clear()
        {
            await Service.UseClient(async context =>
            {
                var client = context.Client;

                await client.DeletePipelineAsync(StoredFilterPipelineId);

                // TODO: Remove
                Placeholder.Todo("Remove the line below before running in production");
                client.DeleteIndex(string.IsNullOrEmpty(Configuration.Prefix) ? Indices.All : Configuration.Prefix + "*").ThrowOnFailure();

                return true;
            });
        }

        public override async Task InitializeAsync()
        {
            if (Configuration.ClearIndicesBeforeUse)
            {
                await Clear();
            }

            if (Configuration.CreateIndices)
            {
                await Service.UseClient(async context =>
                {
                    var client = context.Client;

                    var getPipelineResult = await client.GetPipelineAsync(gp => gp.Id(StoredFilterPipelineId));
                    if (getPipelineResult.IsValid)
                    {
                        return false;
                    }

                    await client.PutPipelineAsync(StoredFilterPipelineId, ppd =>
                        ppd.Processors(pd => pd.BinarySequence<IStoredFilter>(bsp => bsp
                            .IncludeField(sf => sf.StableIds)
                            .UnionField(sf => sf.UnionFilters)
                            .TargetHashField(sf => sf.FilterHash)
                            .TargetCountField(sf => sf.FilterCount)
                            .TargetField(sf => sf.Filter))))
                        .ThrowOnFailure();

                    return true;
                });
            }

            Placeholder.Todo("Configure each store with its specific index sort. Consider defining that on search type");

            foreach (var store in EntityStores)
            {
                // Creates the index
                await store.InitializeAsync();
            }

            await base.InitializeAsync();
        }

        public override async Task<ElasticSearchEntityStore<TSearchType>> CreateStoreAsync<TSearchType>(SearchType searchType)
        {
            var store = new ElasticSearchEntityStore<TSearchType>(this, searchType);
            EntityStores[store.SearchType.Id] = store;
            await Task.CompletedTask;
            return store;
        }

        public async Task<ICodexRepositoryStore> CreateRepositoryStore(Repository repository, Commit commit, Branch branch)
        {
            return new ElasticSearchCodexRepositoryStore(this, repository, commit, branch);
        }
    }

    public class ElasticSearchStoreConfiguration
    {
        /// <summary>
        /// Prefix for indices
        /// </summary>
        public string Prefix = string.Empty;

        /// <summary>
        /// Indicates where indices should be created when <see cref="ElasticSearchStore.InitializeAsync"/> is called.
        /// </summary>
        public bool CreateIndices = true;

        public bool ClearIndicesBeforeUse = true;

        /// <summary>
        /// The number of shards for created indices
        /// </summary>
        public int? ShardCount;

        public string DeclaredDefinitionFilterName { get; } = "AllDeclaredDefs";
    }
}
