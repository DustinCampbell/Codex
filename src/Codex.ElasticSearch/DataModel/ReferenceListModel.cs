﻿using System.Collections.Generic;
using System.Runtime.Serialization;
using Codex.ObjectModel;
using Codex.Utilities;
using static Codex.Utilities.SerializationUtilities;

namespace Codex.Storage.DataModel
{
    public class ReferenceListModel : SpanListModel<ReferenceSpan, SpanListSegmentModel, ReferenceSymbol, ReferenceSymbol>
    {
        public static readonly IEqualityComparer<ReferenceSymbol> ReferenceSymbolEqualityComparer = new EqualityComparerBuilder<ReferenceSymbol>()
            .CompareByAfter(s => s.ProjectId)
            .CompareByAfter(s => s.Id.Value)
            .CompareByAfter(s => s.ReferenceKind);

        public static readonly IComparer<ReferenceSymbol> ReferenceSymbolComparer = new ComparerBuilder<ReferenceSymbol>()
            .CompareByAfter(s => s.ProjectId)
            .CompareByAfter(s => s.Kind)
            .CompareByAfter(s => s.Id.Value)
            .CompareByAfter(s => s.ReferenceKind);

        public static readonly IEqualityComparer<ReferenceSymbol> ReferenceSymbolModelComparer = new EqualityComparerBuilder<ReferenceSymbol>()
            .CompareByAfter(s => s.ProjectId)
            .CompareByAfter(s => s.Id)
            .CompareByAfter(s => s.ReferenceKind);

        public ReferenceListModel()
        {
        }

        public ReferenceListModel(IReadOnlyList<ReferenceSpan> spans)
            : base(spans, ReferenceSymbolEqualityComparer, ReferenceSymbolComparer)
        {
            //PostProcessReferences();
        }

        [OnSerializing]
        public void PostProcessReferences(StreamingContext context)
        {
            string projectId = null;
            string kind = null;
            string referenceKind = null;
            SymbolId id = default(SymbolId);
            foreach (var reference in SharedValues)
            {
                reference.ProjectId = RemoveDuplicate(reference.ProjectId, ref projectId);
                reference.Kind = RemoveDuplicate(reference.Kind, ref kind);
                reference.Id = RemoveDuplicate(reference.Id, ref id);
                reference.ReferenceKind = RemoveDuplicate(reference.ReferenceKind, ref referenceKind);
            }
        }

        [OnDeserialized]
        public void MakeReferences(StreamingContext context)
        {
            string projectId = null;
            string kind = null;
            string referenceKind = null;
            SymbolId id = default(SymbolId);
            foreach (var reference in SharedValues)
            {
                reference.ProjectId = AssignDuplicate(reference.ProjectId, ref projectId);
                reference.Kind = AssignDuplicate(reference.Kind, ref kind);
                reference.Id = AssignDuplicate(reference.Id, ref id);
                reference.ReferenceKind = AssignDuplicate(reference.ReferenceKind, ref referenceKind);
            }
        }

        public override SpanListSegmentModel CreateSegment(ListSegment<ReferenceSpan> segmentSpans)
        {
            return new SpanListSegmentModel();
        }

        public override ReferenceSpan CreateSpan(int start, int length, ReferenceSymbol shared, SpanListSegmentModel segment, int segmentOffset)
        {
            if (shared.ProjectId == null || shared.Kind == null || shared.ReferenceKind == null)
            {
                MakeReferences(default(StreamingContext));
            }

            return new ReferenceSpan()
            {
                Start = start,
                Length = length,
                Reference = shared,
                
                // TODO: Should these be set here
                //LineNumber = 0,
                //LineSpanStart = 0,

                // Definitely DON'T set LineSpanText. Since this structure is intended to be used
                // in source file model which has full text content of source file
                //LineSpanText = null;
            };
        }

        public override ReferenceSymbol GetShared(ReferenceSpan span)
        {
            return span.Reference;
        }

        public override ReferenceSymbol GetSharedKey(ReferenceSpan span)
        {
            return span.Reference;
        }
    }
}
