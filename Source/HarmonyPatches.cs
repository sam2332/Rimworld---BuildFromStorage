using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BuildFromStorage
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        private static FieldInfo placingRotField;
        
        static HarmonyPatches()
        {
            var harmony = new Harmony("buildfromstorage.programmerlily.com");
            harmony.PatchAll();
            
            // Get the placingRot field via reflection since it's protected
            placingRotField = typeof(Designator_Place).GetField("placingRot", BindingFlags.NonPublic | BindingFlags.Instance);
            
            Log.Message("[BuildFromStorage] Harmony patches applied successfully");
        }
        
        public static Rot4 GetPlacingRot(Designator_Build instance)
        {
            return (Rot4)placingRotField.GetValue(instance);
        }
    }

    /// <summary>
    /// Patches Designator_Build.DesignateSingleCell to check for available minified items
    /// before placing a normal build blueprint
    /// </summary>
    [HarmonyPatch(typeof(Designator_Build), "DesignateSingleCell")]
    public static class Designator_Build_DesignateSingleCell_Patch
    {
        public static bool Prefix(Designator_Build __instance, IntVec3 c)
        {
            try
            {
                // Only intercept if we're building a ThingDef (not terrain)
                var thingDef = __instance.PlacingDef as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                    return true; // Continue with normal behavior

                // Check if there are any minified items available in storage
                var map = __instance.Map;
                var stuffDef = __instance.StuffDef;
                
                var availableMinified = MinifiedItemFinder.FindMinifiedInStorage(map, thingDef, stuffDef);
                if (availableMinified == null)
                    return true; // No minified items available, continue with normal behavior

                // Found a minified item! Place an install blueprint instead of a build blueprint
                Log.Message($"[BuildFromStorage] Found minified {thingDef.label} in storage, placing install blueprint");

                // Check if we can place at this location
                var placingRot = HarmonyPatches.GetPlacingRot(__instance);
                var acceptanceReport = GenConstruct.CanPlaceBlueprintAt(thingDef, c, placingRot, map, false, null, availableMinified.InnerThing, stuffDef);
                if (!acceptanceReport.Accepted)
                {
                    Messages.Message(acceptanceReport.Reason, MessageTypeDefOf.RejectInput, false);
                    return false; // Cancel placement
                }

                // Clear any existing things at the location
                GenSpawn.WipeExistingThings(c, placingRot, thingDef.installBlueprintDef, map, DestroyMode.Deconstruct);

                // Place the install blueprint
                var installBlueprint = GenConstruct.PlaceBlueprintForInstall(availableMinified, c, map, placingRot, Faction.OfPlayer);
                
                // Apply any style settings
                if (__instance.sourcePrecept != null)
                {
                    availableMinified.InnerThing.StyleSourcePrecept = __instance.sourcePrecept;
                }
                else if (__instance.styleDef != null)
                {
                    availableMinified.InnerThing.StyleDef = __instance.styleDef;
                }

                // Play effects and notifications
                FleckMaker.ThrowMetaPuffs(GenAdj.OccupiedRect(c, placingRot, thingDef.Size), map);
                
                if (thingDef.IsOrbitalTradeBeacon)
                {
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.BuildOrbitalTradeBeacon, KnowledgeAmount.Total);
                }

                if (TutorSystem.TutorialMode)
                {
                    TutorSystem.Notify_Event(new EventPack(__instance.TutorTagDesignate, c));
                }

                // Call any place workers
                if (thingDef.PlaceWorkers != null)
                {
                    for (int i = 0; i < thingDef.PlaceWorkers.Count; i++)
                    {
                        thingDef.PlaceWorkers[i].PostPlace(map, thingDef, c, placingRot);
                    }
                }

                return false; // Skip the original method
            }
            catch (Exception ex)
            {
                Log.Error($"[BuildFromStorage] Error in Designator_Build patch: {ex}");
                return true; // Fall back to normal behavior on error
            }
        }
    }

    /// <summary>
    /// Patch to show in the build menu tooltip when minified items are available
    /// </summary>
    [HarmonyPatch(typeof(Designator_Build), "DrawPanelReadout")]
    public static class Designator_Build_DrawPanelReadout_Patch
    {
        public static void Postfix(Designator_Build __instance, ref float curY, float width)
        {
            try
            {
                var thingDef = __instance.PlacingDef as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                    return;

                var map = Find.CurrentMap;
                if (map == null)
                    return;

                var stuffDef = __instance.StuffDef;
                var availableCount = MinifiedItemFinder.GetAllAvailableMinified(map, thingDef, stuffDef).Count();
                
                if (availableCount > 0)
                {
                    Text.Font = GameFont.Small;
                    var rect = new Rect(0f, curY, width, 29f);
                    
                    // Draw a background to make it more visible
                    GUI.color = new Color(0.2f, 0.6f, 0.2f, 0.3f);
                    GUI.DrawTexture(rect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                    
                    var labelText = $"Available in storage: {availableCount}";
                    Widgets.Label(rect, labelText);
                    curY += 29f;
                    
                    // Add tooltip explaining the feature
                    if (Mouse.IsOver(rect))
                    {
                        TooltipHandler.TipRegion(rect, "BuildFromStorage will automatically use minified items from storage instead of requiring raw materials for construction.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BuildFromStorage] Error in DrawPanelReadout patch: {ex}");
            }
        }
    }

    /// <summary>
    /// Optional patch to prevent normal construction jobs when minified items are available
    /// This ensures pawns don't try to build with materials when they could install instead
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class WorkGiver_ConstructDeliverResources_ResourceDeliverJobFor_Patch
    {
        public static bool Prefix(WorkGiver_ConstructDeliverResources __instance, Pawn pawn, IConstructible c, ref Job __result)
        {
            try
            {
                // Only check for Blueprint_Build (regular construction), not installations
                var blueprint = c as Blueprint_Build;
                if (blueprint == null)
                    return true;

                var thingDef = blueprint.def.entityDefToBuild as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                    return true;

                // Check if there are minified items available
                var stuffDef = blueprint.stuffToUse;
                var availableMinified = MinifiedItemFinder.FindMinifiedInStorage(pawn.Map, thingDef, stuffDef);
                
                if (availableMinified != null)
                {
                    // There's a minified item available, but the player placed a build blueprint
                    // This could happen if they placed it before the minified item was moved to storage
                    // We'll let the normal construction proceed but log this case
                    Log.Message($"[BuildFromStorage] Note: Blueprint for {thingDef.label} exists but minified item is available in storage");
                }

                return true; // Continue with normal behavior
            }
            catch (Exception ex)
            {
                Log.Error($"[BuildFromStorage] Error in ResourceDeliverJobFor patch: {ex}");
                return true;
            }
        }
    }
}
