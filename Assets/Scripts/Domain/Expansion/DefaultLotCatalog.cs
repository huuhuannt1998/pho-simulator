using System.Collections.Generic;

namespace Pho.Domain.Expansion
{
    /// <summary>
    /// The vertical slice's authored block of lots, in code.
    ///
    /// WHY CODE AND NOT A ScriptableObject: architecture.md section 10 lists
    /// <c>Assets/Content/*.asset</c> as a YAML conflict hotspot whose fix is
    /// "generated from a C# manifest file -- agents edit the manifest, never
    /// the .asset", and the Content/Data folders belong to another agent
    /// entirely. This IS that manifest. It is pure, so the whole adjacency
    /// graph is validated by <c>Tools/test.sh</c> rather than by an EditMode
    /// content test; when the Data agent later authors a <c>LotData</c>
    /// ScriptableObject it projects into <see cref="LotDef"/> and gets handed
    /// to <c>ExpansionService.Bind</c>, which already takes a catalog.
    ///
    /// THE SHAPE OF THE BLOCK (this is deliberately not a straight line, so
    /// the adjacency rule has something to actually constrain):
    /// <code>
    ///     [storeroom]---[unit_1*]---[unit_2]---[unit_3]
    ///                                   |
    ///                              [upstairs]
    /// </code>
    /// <c>unit_1</c> is the shop the player starts in. <c>storeroom</c> and
    /// <c>unit_2</c> both touch it, so the very first purchase is already a
    /// choice. <c>upstairs</c> and <c>unit_3</c> both hang off
    /// <c>unit_2</c> and are unreachable until it is bought -- the "you
    /// cannot buy the far building" case. Prices climb steeply because the
    /// point of the mechanic is that four players pool a shared bank for a
    /// while before the next unit lands.
    /// </summary>
    public static class DefaultLotCatalog
    {
        public static readonly LotId Unit1 = new LotId("lot.unit_1");
        public static readonly LotId Storeroom = new LotId("lot.storeroom");
        public static readonly LotId Unit2 = new LotId("lot.unit_2");
        public static readonly LotId Unit3 = new LotId("lot.unit_3");
        public static readonly LotId Upstairs = new LotId("lot.upstairs");

        /// <summary>Fresh list per call -- <see cref="LotDef"/> is immutable, but the list must not be shared and mutated by a caller.</summary>
        public static List<LotDef> Build()
        {
            return new List<LotDef>
            {
                new LotDef(Unit1, "Phở Shop", 0m, isStarting: true),

                new LotDef(Storeroom, "Back Storeroom", 900m,
                    prerequisites: new[] { Unit1 }),

                new LotDef(Unit2, "Unit 2 -- Next Door", 2500m,
                    prerequisites: new[] { Unit1 }),

                new LotDef(Unit3, "Unit 3 -- Corner Shop", 6000m,
                    prerequisites: new[] { Unit2 }),

                new LotDef(Upstairs, "Upstairs Dining Room", 4500m,
                    prerequisites: new[] { Unit2 }),
            };
        }

        /// <summary>Convenience: the built, validated registry.</summary>
        public static LotRegistry BuildRegistry() => new LotRegistry(Build());
    }
}
