using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace EchoColony
{
    /// <summary>
    /// The 7 levels of clothing/nudity precepts from the RimWorld *Ideology* DLC. 
    /// They apply independently for men (Nudity_Male) and women (Nudity_Female).
    /// </summary>
    public enum NudityPreceptLevel
    {
        None,            // No active ideology or specific rules
        Mandatory,       // 1. Completely nude (Nudity_*_Mandatory)
        PantsAtMost,     // 2. Pants as maximum; torso uncovered mandatory (CoveringAnythingButGroinDisapproved)
        NoRules,         // 3. No rules; being nude or clothed is indifferent (Nudity_*_NoRules)
        Pants,           // 4. Pants mandatory; torso optional (UncoveredGroinDisapproved)
        PantsAndShirt,   // 5. Pants and shirt mandatory (UncoveredGroinOrChestDisapproved)
        PantsShirtHat,   // 6. Body and hair must be covered (UncoveredGroinChestOrHairDisapproved)
        TotallyCovered   // 7. Body, hair and face must be covered (UncoveredGroinChestHairOrFaceDisapproved)
    }

    /// <summary>
    /// Structure that captures the actual physical coverage of the body according to the equipped apparel.
    /// </summary>
    public struct PawnClothingCoverage
    {
        public bool HasApparel;
        public bool GroinCovered;
        public bool TorsoCovered;
        public bool HairCovered;
        public bool FaceCovered;

        // It is considered bodily nudity if the torso and groin are exposed (even if the person is wearing an accessory or a hat).
        public bool IsCompletelyNude => !GroinCovered && !TorsoCovered;
        public bool IsBareChestedWithPants => GroinCovered && !TorsoCovered;
        public bool IsShirtOnlyNoPants => !GroinCovered && TorsoCovered;
        public bool IsStandardCovered => GroinCovered && TorsoCovered;
    }

    public static class ClothingPromptClassifier
    {
        /// <summary>
        /// A delegate that allows external modules or compatibility patches
        /// to modify the evaluated coverage without coupling the base classifier to third-party mods.
        /// </summary>
        public delegate void ClothingCoverageModifier(Pawn pawn, ref PawnClothingCoverage coverage);
        public static event ClothingCoverageModifier OnCoverageEvaluated;

        /// <summary>
        /// Analyzes the apparel equipped by the colonist to determine which body parts are covered. 
        /// Uses exclusively the engine's official BodyPartGroupDefOf (100% language-independent)
        /// and the Ideology expansion's modesty system.
        /// </summary>
        public static PawnClothingCoverage GetCoverage(Pawn pawn)
        {
            var cov = new PawnClothingCoverage();
            if (pawn?.apparel?.WornApparel == null || !pawn.apparel.WornApparel.Any())
                return cov;

            cov.HasApparel = true;

            // 1. Official native check from RimWorld (language-agnostic)
            // ThoughtWorker_Precept_GroinUncovered.HasUncoveredGroin is the canonical method in Ideology. 
            // If it returns false, the groin is considered covered according to the game's official rules.
            bool vanillaGroinCovered = false;
            try
            {
                if (ModsConfig.IdeologyActive)
                {
                    vanillaGroinCovered = !ThoughtWorker_Precept_GroinUncovered.HasUncoveredGroin(pawn);
                }
                else
                {
                    vanillaGroinCovered = pawn.apparel.BodyPartGroupIsCovered(BodyPartGroupDefOf.Legs);
                }
            }
            catch
            {
                vanillaGroinCovered = false;
            }

            if (vanillaGroinCovered)
            {
                cov.GroinCovered = true;
            }

            // Native torso verification
            if (pawn.apparel.BodyPartGroupIsCovered(BodyPartGroupDefOf.Torso))
            {
                cov.TorsoCovered = true;
            }

            // 2. Iteration over each piece of apparel using official DefOfs (without relying on language-specific strings)
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel?.def?.apparel == null) continue;
                if (!apparel.def.apparel.countsAsClothingForNudity) continue;
                if (apparel.def.apparel.bodyPartGroups == null) continue;

                foreach (var group in apparel.def.apparel.bodyPartGroups)
                {
                    // Legs / Groin (Official RimWorld DefOf)
                    if (group == BodyPartGroupDefOf.Legs)
                    {
                        cov.GroinCovered = true;
                    }
                    // Torso / Chest (Official RimWorld DefOf)
                    else if (group == BodyPartGroupDefOf.Torso)
                    {
                        cov.TorsoCovered = true;
                    }
                    // Head / Hair (Official RimWorld DefOf)
                    else if (group == BodyPartGroupDefOf.UpperHead || group == BodyPartGroupDefOf.FullHead)
                    {
                        cov.HairCovered = true;
                    }
                    // Face (Official RimWorld DefOf)
                    else if (group == BodyPartGroupDefOf.Eyes)
                    {
                        cov.FaceCovered = true;
                    }
                    // Waist. If an apparel piece covers Waist, it covers the pelvic/lower body part (e.g., corsets, tribal skirts).
                    else if (group.defName == "Waist")
                    {
                        bool isClothingLayer = apparel.def.apparel.layers != null &&
                            (apparel.def.apparel.layers.Contains(ApparelLayerDefOf.OnSkin) ||
                             apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Middle) ||
                             apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Shell));

                        if (isClothingLayer)
                        {
                            cov.GroinCovered = true;
                        }
                    }
                }
            }

            // 3. Notify registered patches and compatibility extensions
            try
            {
                OnCoverageEvaluated?.Invoke(pawn, ref cov);
            }
            catch (Exception ex)
            {
                Log.Warning($"[EchoColony] Error in OnCoverageEvaluated modifier: {ex.Message}");
            }

            return cov;
        }

        /// <summary>
        /// Determines the level of dress or nudity that the ideology imposes upon the specified gender.
        /// </summary>
        public static NudityPreceptLevel GetNudityLevel(Ideo ideo, Gender gender)
        {
            if (ideo == null || !ModsConfig.IdeologyActive) return NudityPreceptLevel.None;

            string issueName = gender == Gender.Female ? "Nudity_Female" : "Nudity_Male";
            var precept = ideo.PreceptsListForReading?.FirstOrDefault(p => p.def?.issue?.defName == issueName);
            if (precept?.def == null) return NudityPreceptLevel.None;

            string defName = precept.def.defName;
            if (defName.EndsWith("_Mandatory", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.Mandatory;
            if (defName.IndexOf("CoveringAnythingButGroinDisapproved", StringComparison.OrdinalIgnoreCase) >= 0)
                return NudityPreceptLevel.PantsAtMost;
            if (defName.EndsWith("_NoRules", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.NoRules;
            if (defName.EndsWith("_UncoveredGroinDisapproved", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.Pants;
            if (defName.EndsWith("_UncoveredGroinOrChestDisapproved", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.PantsAndShirt;
            if (defName.EndsWith("_UncoveredGroinChestOrHairDisapproved", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.PantsShirtHat;
            if (defName.EndsWith("_UncoveredGroinChestHairOrFaceDisapproved", StringComparison.OrdinalIgnoreCase))
                return NudityPreceptLevel.TotallyCovered;

            return NudityPreceptLevel.None;
        }

        /// <summary>
        /// SELF-PERCEPTION: Returns the tag that injects into the prompt the colonist’s awareness of their own attire—or 
        /// lack thereof—evaluating their ideology, traits, and thermal survival. 
        /// If the colonist is clothed and fully complies with their own standards, it returns null (zero tokens consumed).
        /// </summary>
        public static string GetSelfClothingPerception(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps?.Animal == true) return null;

            var cov = GetCoverage(pawn);
            bool isNudist = pawn.story?.traits != null && pawn.story.traits.allTraits.Any(t => t?.def?.defName == "Nudist");
            var preceptLevel = GetNudityLevel(pawn.Ideo, pawn.gender);

            // Evaluates whether ambient temperature is dangerous, warranting protective clothing regardless of beliefs or traits.
            bool unsafeTemperature = false;
            try
            {
                var tempRange = pawn.ComfortableTemperatureRange();
                unsafeTemperature = pawn.AmbientTemperature < tempRange.min || pawn.AmbientTemperature > tempRange.max;
            }
            catch { }

            // ==========================================
            // CASE 1: Completely naked
            // ==========================================
            if (cov.IsCompletelyNude)
            {
                if (isNudist)
                    return "[Clothing: Completely naked — You are a nudist; you feel blissful, liberated, and comfortable in your bare skin.]";
                if (preceptLevel == NudityPreceptLevel.Mandatory)
                    return "[Clothing: Completely naked — In pious accordance with your sacred nudity beliefs; you feel pure and spiritually aligned.]";
                if (preceptLevel == NudityPreceptLevel.NoRules)
                    return "[Clothing: Completely naked — Casual and unconcerned; your culture has no rules about clothing.]";

                // Vanilla fallback: completely unclad, feeling vulnerable or embarrassed
                return "[Clothing: Completely naked — You are completely exposed and wearing no clothes; you feel vulnerable, self-conscious, and uncomfortable being seen nude.]";
            }

            // ==========================================
            // CASE 2: Bare-chested with pants
            // ==========================================
            if (cov.IsBareChestedWithPants)
            {
                // Precept: Mandatory total nudity
                if (preceptLevel == NudityPreceptLevel.Mandatory)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Bare-chested with pants — You wear pants strictly for survival against extreme temperature, though your faith demands complete nudity.]";
                    return "[Clothing: Bare-chested with pants — Wearing pants violates your sacred total nudity beliefs; you feel spiritually constrained.]";
                }

                // Trait: Nudist
                if (isNudist)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Bare-chested with pants — You wear pants only to survive the harsh temperature, though as a nudist you yearn to be completely naked.]";
                    return "[Clothing: Bare-chested with pants — While you enjoy having your chest bare, as a nudist you would much rather be completely naked.]";
                }

                // Precept: Pants at most (exact compliance with faith)
                if (preceptLevel == NudityPreceptLevel.PantsAtMost)
                    return "[Clothing: Bare-chested with pants — In exact accordance with your faith (groin covered, chest bare).]";

                // Modesty precepts: Pants and shirt, or stricter
                if (preceptLevel >= NudityPreceptLevel.PantsAndShirt)
                    return "[Clothing: Bare-chested — Your torso is exposed against your beliefs of modesty; you feel self-conscious and exposed.]";

                // NoRules and standard fallback
                return "[Clothing: Bare-chested with pants.]";
            }

            // ==========================================
            // CASE 3: Shirt only (no pants)
            // ==========================================
            if (cov.IsShirtOnlyNoPants)
            {
                // Precept: Mandatory total nudity
                if (preceptLevel == NudityPreceptLevel.Mandatory)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Shirt only (no pants) — Wearing a shirt violates your sacred nudity faith, endured only for survival against extreme temperature, with your lower body bare.]";
                    return "[Clothing: Shirt only (no pants) — Wearing a shirt is a grievous violation of your sacred nudity faith; you feel stifled, while your lower body is completely bare.]";
                }

                // Trait: Nudist
                if (isNudist)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Shirt only (no pants) — You wear a shirt only to survive the harsh temperature, but as a nudist you feel stifled and uncomfortable by it, with your legs bare.]";
                    return "[Clothing: Shirt only (no pants) — As a nudist, wearing a shirt feels stifling and irritating, while your lower body is completely bare.]";
                }

                // Precept: No rules (indifferent)
                if (preceptLevel == NudityPreceptLevel.NoRules)
                    return "[Clothing: Shirt only (no pants) — Wearing only a shirt; casual and indifferent as your culture has no clothing rules.]";

                // All other ideology precepts (Pants, PantsAndShirt, TotallyCovered, PantsAtMost)
                if (preceptLevel != NudityPreceptLevel.None)
                    return "[Clothing: Shirt only (no pants) — Your lower body is completely exposed; this is a sinful violation of your faith's modesty doctrine and you feel vulnerable, sinful, and ashamed.]";

                // Vanilla fallback
                return "[Clothing: Shirt only (no pants) — Your lower body is completely exposed; you feel awkward, vulnerable, and deeply humiliated without pants.]";
            }

            // ==========================================
            // CASE 4: Standard covered (groin and torso covered)
            // ==========================================
            if (cov.IsStandardCovered)
            {
                // Precept: Mandatory total nudity
                if (preceptLevel == NudityPreceptLevel.Mandatory)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Clothed for survival — Wearing clothes violates your sacred nudity faith, but extreme weather forces you to endure them to survive.]";
                    return "[Clothing: Clothed — Wearing clothes is a grievous violation of your sacred nudity faith; you feel constrained, dirty, and stifled by these garments.]";
                }

                // Trait: Nudist
                if (isNudist)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Clothed for survival — You are wearing clothes only to survive the harsh temperature, but as a nudist you feel constrained and hate being covered up.]";
                    return "[Clothing: Clothed — You feel constrained and uncomfortable wearing clothes; as a nudist you hate being covered up.]";
                }

                // Precept: Pants at most
                if (preceptLevel == NudityPreceptLevel.PantsAtMost)
                {
                    if (unsafeTemperature)
                        return "[Clothing: Torso covered for survival — Wearing a shirt violates your faith's requirement for a bare chest, but extreme weather forces you to endure it to survive.]";
                    return "[Clothing: Torso covered — Wearing a shirt violates your faith's requirement for a bare chest; you feel stifled, sinful, and unnatural.]";
                }

                // Precept: Total coverage (body, face, and hair)
                if (preceptLevel == NudityPreceptLevel.TotallyCovered)
                {
                    if (!cov.FaceCovered && !cov.HairCovered)
                        return "[Clothing: Head and face exposed — Your face and hair are completely uncovered; having part of your head or face exposed violates your religious doctrine that demands total concealment.]";
                    if (!cov.FaceCovered || !cov.HairCovered)
                        return "[Clothing: Head partly exposed — Part of your head or face is uncovered, violating your religious doctrine that demands total concealment.]";
                }

                // Precept: Mandatory hair coverage
                if (preceptLevel == NudityPreceptLevel.PantsShirtHat && !cov.HairCovered)
                    return "[Clothing: Hair exposed — Your hair is uncovered, violating your doctrine that demands head/hair covering.]";

                // Complies with beliefs and standard attire: returns null (zero tokens)
                return null;
            }

            return null;
        }

        /// <summary>
        /// SOCIAL PERCEPTION: Evaluates the impact of the observed pawn's attire against the observer's beliefs.
        /// Injected into the relationship dynamics section (=== RELATIONSHIP & POWER DYNAMIC ===).
        /// Returns null (zero tokens consumed) if the observed pawn complies with the observer's standards.
        /// </summary>
        public static string GetSocialClothingReaction(Pawn observer, Pawn observed)
        {
            if (observer == null || observed == null || observer == observed || observed.RaceProps?.Animal == true)
                return null;

            var cov = GetCoverage(observed);
            var precept = GetNudityLevel(observer.Ideo, observed.gender);

            // 1. Observer's faith requires mandatory total nudity
            if (precept == NudityPreceptLevel.Mandatory)
            {
                if (cov.HasApparel && (cov.GroinCovered || cov.TorsoCovered))
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observer.LabelShort}'s faith demands total nudity for {observed.gender.GetLabel()}s; {observer.LabelShort} looks upon {observed.LabelShort}'s clothing with disgust and pity.";
                return null;
            }

            // 2. Observer's faith requires pants at most (bare chest required)
            if (precept == NudityPreceptLevel.PantsAtMost)
            {
                if (cov.TorsoCovered)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observer.LabelShort}'s faith demands that {observed.gender.GetLabel()}s keep their chest bare; {observer.LabelShort} views {observed.LabelShort}'s covered torso as stifling and unnatural.";
                if (!cov.GroinCovered)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort}'s groin is uncovered, violating {observer.LabelShort}'s religious rule that pants must be worn.";
                return null;
            }

            // 3. Observer's faith requires modesty (pants and shirt or stricter)
            if (precept >= NudityPreceptLevel.PantsAndShirt)
            {
                if (cov.IsCompletelyNude)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort} is completely naked, which deeply offends {observer.LabelShort}'s religious modesty convictions.";
                if (!cov.TorsoCovered)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort}'s chest is exposed, violating {observer.LabelShort}'s sacred modesty rules.";
                if (precept == NudityPreceptLevel.TotallyCovered)
                {
                    if (!cov.FaceCovered && !cov.HairCovered)
                        return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort}'s head and face are uncovered, which {observer.LabelShort}'s faith considers deeply immodest and shameful.";
                    if (!cov.FaceCovered || !cov.HairCovered)
                        return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: Part of {observed.LabelShort}'s head or face is exposed, violating {observer.LabelShort}'s total concealment doctrine.";
                }
                if (precept == NudityPreceptLevel.PantsShirtHat && !cov.HairCovered)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort}'s hair is uncovered, violating {observer.LabelShort}'s modesty doctrine.";
                return null;
            }

            // 4. Observer's faith requires at least pants (torso optional)
            if (precept == NudityPreceptLevel.Pants)
            {
                if (!cov.GroinCovered)
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort}'s groin is uncovered, violating {observer.LabelShort}'s religious rule that pants must be worn.";
                return null;
            }

            // 5. Observer without strict ideology (or NoRules), but the observed pawn is completely naked
            if (cov.IsCompletelyNude && precept != NudityPreceptLevel.NoRules)
            {
                bool observerIsNudist = observer.story?.traits != null && observer.story.traits.allTraits.Any(t => t?.def?.defName == "Nudist");
                if (!observerIsNudist)
                {
                    return $"{observer.LabelShort}'s reaction to {observed.LabelShort}: {observed.LabelShort} is completely naked; {observer.LabelShort} can't help but feel uncomfortable or distracted by their exposed body.";
                }
            }

            return null;
        }
    }
}
