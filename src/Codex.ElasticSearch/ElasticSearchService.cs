﻿using Nest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Codex.Utilities;
using Elasticsearch.Net;
using Newtonsoft.Json;
using Codex.Serialization;
using Codex.Storage.ElasticProviders;
using Codex.ElasticSearch.Utilities;
using Codex.ObjectModel;
using Codex.ElasticSearch.Search;

namespace Codex.ElasticSearch
{
    public class ElasticSearchService
    {
        private ElasticClient client;
        private Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly ElasticSearchServiceConfiguration configuration;
        private readonly ConnectionSettings settings;

        public ElasticSearchService(ElasticSearchServiceConfiguration configuration)
        {
            this.configuration = configuration;
            this.settings = new OverrideConnectionSettings(new Uri(configuration.Endpoint))
                .EnableHttpCompression();

            if (configuration.CaptureRequests)
            {
                settings = settings.PrettyJson().DisableDirectStreaming().MapDefaultTypeNames(typeNames =>
                {
                    foreach (var searchType in SearchTypes.RegisteredSearchTypes)
                    {
                        typeNames[searchType.Type] = searchType.Name.ToLowerInvariant();
                        typeNames[ElasticCodexTypeUtilities.Instance.GetImplementationType(searchType.Type)] = searchType.Name.ToLowerInvariant();
                    }
                });
            }

            foreach (var searchType in SearchTypes.RegisteredSearchTypes)
            {
                var mapper = (IdMapper)Activator.CreateInstance(typeof(IdMapper<>).MakeGenericType(ElasticCodexTypeUtilities.Instance.GetImplementationType(searchType.Type)));
                settings = mapper.MapId(settings);
            }

            client = new ElasticClient(settings);
        }

        public async Task<ElasticSearchResponse<T>> UseClient<T>(Func<ClientContext, Task<T>> useClient)
        {
            var startTime = stopwatch.Elapsed;
            T result;
            var context = new ClientContext()
            {
                CaptureRequests = configuration.CaptureRequests,
                Client = this.client
            };

            result = await useClient(context);

            return new ElasticSearchResponse<T>()
            {
                Requests = context.Requests,
                Duration = stopwatch.Elapsed - startTime,
                Result = result
            };
        }

        public Task ClearAsync()
        {
            return DeleteIndexAsync(Indices.All);
        }

        public async Task<bool> DeleteIndexAsync(Indices indices)
        {
            var result = await UseClient(async context =>
            {
                var existsQuery = (await client.IndexExistsAsync(indices)).ThrowOnFailure();
                if (!existsQuery.Exists)
                {
                    return false;
                }

                var response = await context.Client.DeleteIndexAsync(indices);
                response.ThrowOnFailure();
                return true;
            });

            return result.Result;
        }

        public async Task<IEnumerable<(string IndexName, bool IsActive)>> GetIndicesAsync()
        {
            var response = await UseClient(async context =>
            {
                var client = context.Client;

                var result = await client.GetAliasAsync().ThrowOnFailure();

                return result.Indices.Select(kvp =>
                (
                    IndexName: kvp.Key,
                    IsActive: Placeholder.Value<bool>("Is this still applicable?")
                )).OrderBy(v => v.IndexName, StringComparer.OrdinalIgnoreCase).ToList();
            });

            return response.Result;
        }

        public async Task<ElasticSearchStore> CreateStoreAsync(ElasticSearchStoreConfiguration configuration)
        {
            var store = new ElasticSearchStore(configuration, this);
            await store.InitializeAsync();
            return store;
        }

        public async Task<ElasticSearchCodex> CreateCodexAsync(ElasticSearchStoreConfiguration configuration)
        {
            var store = new ElasticSearchCodex(configuration, this);
            return store;
        }
    }

    public class OverrideConnectionSettings : ConnectionSettings
    {
        private static Serializer SharedSerializer;
        public OverrideConnectionSettings(Uri uri) : base(new SingleNodeConnectionPool(uri), SerializerFactory.CreateCore)
        {
        }

        public JsonNetSerializer GetSerializer()
        {
            return new Serializer(this);
        }

        private class SerializerFactory : ISerializerFactory
        {
            public IElasticsearchSerializer Create(IConnectionSettingsValues settings)
            {
                return SerializerFactory.CreateCore(settings);
            }

            public IElasticsearchSerializer CreateStateful(IConnectionSettingsValues settings, JsonConverter converter)
            {
                return new Serializer(settings, converter);
            }

            public static IElasticsearchSerializer CreateCore(IConnectionSettingsValues settings)
            {
                if (SharedSerializer == null)
                {
                    SharedSerializer = new Serializer(settings);
                }

                return SharedSerializer;
            }
        }

        private class Serializer : JsonNetSerializer
        {
            public Serializer(IConnectionSettingsValues settings)
                : base(settings, ModifyJsonSerializerSettings)
            {
            }

            public Serializer(IConnectionSettingsValues settings, JsonConverter statefulConverter)
                : base(settings, statefulConverter, ModifyJsonSerializerSettings)
            {
            }

            private static void ModifyJsonSerializerSettings(JsonSerializerSettings arg1, IConnectionSettingsValues arg2)
            {
                arg1.ContractResolver = new CachingElasticContractResolver(new CompositeEntityResolver(
                    new EntityContractResolver(ObjectStage.Index),
                    arg1.ContractResolver), arg2, null);
            }
        }
    }

    public class ClientContext
    {
        // TODO: Disable
        public bool CaptureRequests = true;
        public ElasticClient Client;
        public List<string> Requests = new List<string>();
    }

    public class ElasticSearchResponse<T>
    {
        public IReadOnlyList<string> Requests { get; set; } = CollectionUtilities.Empty<string>.Array;
        public Exception Exception { get; set; }
        public T Result { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class ElasticSearchServiceConfiguration
    {
        public string Endpoint { get; set; }
        public bool CaptureRequests { get; set; } = true;

        public ElasticSearchServiceConfiguration(string endpoint)
        {
            Endpoint = endpoint;
        }
    }
}
