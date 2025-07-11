using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BuildFromStorage
{
    public static class MinifiedItemFinder
    {
        /// <summary>
        /// Searches all storage on the map for minified items that match the given ThingDef
        /// </summary>
        /// <param name="map">The map to search</param>
        /// <param name="targetDef">The ThingDef we're looking for</param>
        /// <param name="targetStuff">Optional stuff material that must match</param>
        /// <returns>The first available minified item, or null if none found</returns>
        public static MinifiedThing FindMinifiedInStorage(Map map, ThingDef targetDef, ThingDef targetStuff = null)
        {
            if (map == null || targetDef == null)
                return null;

            // Search through all minified things on the map
            var allMinifiedThings = map.listerThings.ThingsOfDef(ThingDefOf.MinifiedThing)
                .Cast<MinifiedThing>()
                .Where(m => m != null && m.InnerThing != null);

            foreach (var minified in allMinifiedThings)
            {
                // Check if this minified thing matches what we're looking for
                if (minified.InnerThing.def == targetDef)
                {
                    // If stuff is specified, make sure it matches
                    if (targetStuff != null && minified.InnerThing.Stuff != targetStuff)
                        continue;

                    // Make sure it's in storage and accessible
                    if (IsInAccessibleStorage(minified, map))
                    {
                        return minified;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a minified thing is in accessible storage
        /// </summary>
        private static bool IsInAccessibleStorage(MinifiedThing minified, Map map)
        {
            if (!minified.Spawned)
                return false;

            // Check if it's in a storage building or stockpile zone
            var position = minified.Position;
            
            // Check if it's in a building that can store things
            var building = position.GetFirstBuilding(map);
            if (building is Building_Storage storage)
            {
                return storage.GetStoreSettings()?.AllowedToAccept(minified) == true;
            }

            // Check if it's in a stockpile zone
            var zone = map.zoneManager.ZoneAt(position);
            if (zone is Zone_Stockpile stockpile)
            {
                return stockpile.GetStoreSettings()?.AllowedToAccept(minified) == true;
            }

            // If it's just lying around (not forbidden), consider it accessible
            return !minified.IsForbidden(Faction.OfPlayer);
        }

        /// <summary>
        /// Gets all available minified items for a given building definition
        /// </summary>
        public static IEnumerable<MinifiedThing> GetAllAvailableMinified(Map map, ThingDef targetDef, ThingDef targetStuff = null)
        {
            if (map == null || targetDef == null)
                yield break;

            var allMinifiedThings = map.listerThings.ThingsOfDef(ThingDefOf.MinifiedThing)
                .Cast<MinifiedThing>()
                .Where(m => m != null && m.InnerThing != null);

            foreach (var minified in allMinifiedThings)
            {
                if (minified.InnerThing.def == targetDef)
                {
                    if (targetStuff != null && minified.InnerThing.Stuff != targetStuff)
                        continue;

                    if (IsInAccessibleStorage(minified, map))
                    {
                        yield return minified;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if there are any available minified items for the given building
        /// </summary>
        public static bool HasAvailableMinified(Map map, ThingDef targetDef, ThingDef targetStuff = null)
        {
            return FindMinifiedInStorage(map, targetDef, targetStuff) != null;
        }
    }
}
