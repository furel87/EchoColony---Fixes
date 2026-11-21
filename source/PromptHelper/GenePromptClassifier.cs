using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace EchoColony
{
    [StaticConstructorOnStartup]
    public static class GenePromptClassifier
    {
        private static readonly Dictionary<string, string> CustomGeneDescriptions = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> LabelOverrides = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> CustomCategory = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> CustomXenotypeDescriptions = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> BehavioralDirectives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> IgnoredGenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static int lastKnownDefCount = -1;

        private static readonly Regex HtmlTagRegex = new Regex("<.*?>", RegexOptions.Compiled);

        private static readonly HashSet<string> BasicAppearanceKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Hair_","Skin_","BS_Skin","Melanin","Eye_","Beard_","EyeColor","HairColor","SkinColor","Beauty_"
        };

        private static readonly HashSet<string> BasicAppearanceCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HairStyle", "BeardStyle", "Cosmetic_Color", "AG_Cosmetic_Color", "Cosmetic_Skin", "Cosmetic_Hair"
        };

        private static readonly HashSet<string> MutationAppearanceKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wing", "Tail", "Horn", "BS_Horns", "Ears_", "Ear_", "Nose_", "Noses_",
            "BodyScale", "BodyFrame", "Body_Size", "Body_Size_Multiplier", "Body_Fat",
            "Body_Hulk", "Body_Thin", "Hands_", "Hand_", "Fur", "Furskin", "Brow_",
            "FacialRidges", "Jaw_", "Headbone_", "Head_Gaunt", "VoicePig", "VoiceRoar"
        };

        private static readonly HashSet<string> MechanicalCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Reproduction", "Archite", "Metabolism", "Hemogen", "Pain", "Healing",
            "Misc", "Aptitudes", "BS_DO_NOT", "BS_Pilotable", "BS_Production"
        };


        static GenePromptClassifier()
        {
            EnsureDefinitionsLoaded();
        }

        /// <summary>
        /// Checks whether the RimWorld Defs database has loaded or updated gene definitions. 
        /// If the count differs, it synchronizes and reloads the dictionaries dynamically.
        /// </summary>
        public static void EnsureDefinitionsLoaded()
        {
            int currentDefCount = DefDatabase<GeneAIPromptDef>.DefCount;
            if (currentDefCount != lastKnownDefCount)
            {
                LoadXMLDefinitions();
                lastKnownDefCount = currentDefCount;
                Log.Message($"[EchoColony] Dynamic Gene XML Definitions Synced -> {currentDefCount} defs found ({BehavioralDirectives.Count} directives, {CustomCategory.Count} categories, {CustomGeneDescriptions.Count} descriptions).");
            }
        }

        public static bool HasBehavioralDirective(Gene gene)
        {
            if (gene?.def == null) return false;
            EnsureDefinitionsLoaded();
            return BehavioralDirectives.TryGetValue(gene.def.defName, out var d) && !string.IsNullOrWhiteSpace(d);
        }

        public static bool HasBehavioralDirective(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return false;
            EnsureDefinitionsLoaded();
            return BehavioralDirectives.TryGetValue(defName, out var d) && !string.IsNullOrWhiteSpace(d);
        }

        private static void LoadXMLDefinitions()
        {
            CustomGeneDescriptions.Clear();
            LabelOverrides.Clear();
            CustomCategory.Clear();
            BehavioralDirectives.Clear();
            CustomXenotypeDescriptions.Clear();
            IgnoredGenes.Clear();

            var allPromptDefs = DefDatabase<GeneAIPromptDef>.AllDefsListForReading;
            if (allPromptDefs != null)
            {
                foreach (var promptDef in allPromptDefs)
                {
                    if (string.IsNullOrEmpty(promptDef.targetGene)) continue;

                    // If it is explicitly marked as "ignore" or assigned the category "Ignore/Excluded/None"
                    if (promptDef.ignore ||
                        (!string.IsNullOrEmpty(promptDef.customCategory) &&
                         (promptDef.customCategory.Equals("Ignore", StringComparison.OrdinalIgnoreCase) ||
                          promptDef.customCategory.Equals("Ignored", StringComparison.OrdinalIgnoreCase) ||
                          promptDef.customCategory.Equals("Exclude", StringComparison.OrdinalIgnoreCase) ||
                          promptDef.customCategory.Equals("Excluded", StringComparison.OrdinalIgnoreCase) ||
                          promptDef.customCategory.Equals("None", StringComparison.OrdinalIgnoreCase))))
                    {
                        IgnoredGenes.Add(promptDef.targetGene);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(promptDef.customLabel))
                        LabelOverrides[promptDef.targetGene] = promptDef.customLabel;

                    if (!string.IsNullOrEmpty(promptDef.customCategory))
                        CustomCategory[promptDef.targetGene] = promptDef.customCategory;

                    if (!string.IsNullOrEmpty(promptDef.promptDescription))
                        CustomGeneDescriptions[promptDef.targetGene] = promptDef.promptDescription;

                    if (!string.IsNullOrEmpty(promptDef.behavioralDirective))
                        BehavioralDirectives[promptDef.targetGene] = promptDef.behavioralDirective;
                }
            }

            var allXenoDefs = DefDatabase<XenotypeAIPromptDef>.AllDefsListForReading;
            if (allXenoDefs != null)
            {
                foreach (var xenoDef in allXenoDefs)
                {
                    if (!string.IsNullOrEmpty(xenoDef.targetXenotype) && !string.IsNullOrEmpty(xenoDef.promptDescription))
                    {
                        CustomXenotypeDescriptions[xenoDef.targetXenotype] = xenoDef.promptDescription;
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether a gene represents a standard human anatomical feature by default
        /// (such as human ears, a normal jaw, normal hands, or a standard body). 
        /// These genes should be omitted so as not to describe basic human anatomy as a mutation or a trait.
        /// </summary>
        public static bool IsBaselineHumanGene(GeneDef def)
        {
            if (def == null) return false;
            string name = def.defName;
            return name.EndsWith("_Human", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_Baseline", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Body_Standard", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBaselineHumanGene(Gene gene) => IsBaselineHumanGene(gene?.def);

        /// <summary>
        /// Check whether a gene has been explicitly marked in the XML to be ignored or excluded from all prompts.
        /// </summary>
        public static bool IsIgnoredGene(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return false;
            EnsureDefinitionsLoaded();
            return IgnoredGenes.Contains(defName);
        }

        public static bool IsIgnoredGene(GeneDef def) => def != null && IsIgnoredGene(def.defName);
        public static bool IsIgnoredGene(Gene gene) => IsIgnoredGene(gene?.def?.defName);

        public static List<Gene> GetActiveGenes(Pawn pawn)
        {
            if (pawn?.genes?.GenesListForReading == null)
            {
                return new List<Gene>();
            }

            return pawn.genes.GenesListForReading
                .Where(g => g.Active)
                .ToList();
        }

        private struct GeneDisplayInfo
        {
            public string Label;
            public string Description; 
        }

        /// <summary>
        /// Centralizes label resolution (with XML overrides) and descriptions (custom XML → truncated native fallback). 
        /// Eliminates duplication across FormatGeneForPrompt, FormatGeneForBulletList, and BuildVisualAppearancePrompt.
        /// </summary>
        private static GeneDisplayInfo ResolveGeneDisplay(Gene gene, bool includeFallbackDesc = true, int maxDescLength = 80)
        {
            string defName = gene.def.defName;
            string label = LabelOverrides.TryGetValue(defName, out var customLabel) ? customLabel : gene.def.label;

            if (CustomGeneDescriptions.TryGetValue(defName, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc))
                return new GeneDisplayInfo { Label = label, Description = customDesc.Trim() };

            if (!includeFallbackDesc)
                return new GeneDisplayInfo { Label = label, Description = null };

            string desc = gene.def.description;
            if (!string.IsNullOrEmpty(desc))
            {
                desc = HtmlTagRegex.Replace(desc, "").Trim();
                int periodIndex = desc.IndexOf('.');
                if (periodIndex > 0) desc = desc.Substring(0, periodIndex);
                if (desc.Length > maxDescLength) desc = desc.Substring(0, maxDescLength - 3) + "...";
                return new GeneDisplayInfo { Label = label, Description = desc };
            }

            return new GeneDisplayInfo { Label = label, Description = null };
        }

        /// <summary>
        /// Format the gene for the prompt. Prioritize the custom XML; if it does not exist, use the truncated native description.
        /// </summary>
        public static string FormatGeneForPrompt(Gene gene, bool includeFallbackDesc = true)
        {
            if (gene?.def == null || IsIgnoredGene(gene)) return string.Empty;
            EnsureDefinitionsLoaded();

            var info = ResolveGeneDisplay(gene, includeFallbackDesc);
            if (string.IsNullOrEmpty(info.Label)) return string.Empty;
            return info.Description != null ? $"{info.Label} ({info.Description})" : info.Label;
        }

        /// <summary>
        /// Retrieves the optimized description for the xenotype:
        /// 1. Priority: Definition in XML (XenotypeAIPromptDef)
        /// 2. Fallback: First paragraph of the game's native description (without HTML tags)
        /// </summary>
        public static string GetXenotypeDescription(XenotypeDef xeno)
        {
            if (xeno == null || xeno == XenotypeDefOf.Baseliner) return null;
            EnsureDefinitionsLoaded();

            // 1. We prioritize the supplementary XML (Ideal for mods)
            if (CustomXenotypeDescriptions.TryGetValue(xeno.defName, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc))
            {
                return customDesc.Trim();
            }

            // 2. FALLBACK: If there is no XML, we extract the first native paragraph.
            string rawDesc = xeno.description;
            if (!string.IsNullOrEmpty(rawDesc))
            {
                rawDesc = HtmlTagRegex.Replace(rawDesc, "").Trim();
                string[] paragraphs = rawDesc.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (paragraphs.Length > 0)
                {
                    return paragraphs[0].Trim();
                }
            }

            return null;
        }

        public static string GetXenotypeDescription(Pawn pawn)
        {
            if (pawn?.genes?.Xenotype == null || pawn.genes.Xenotype == XenotypeDefOf.Baseliner)
                return null;

            return GetXenotypeDescription(pawn.genes.Xenotype);
        }

        public static string GetXenotypeInfo(Pawn pawn)
        {
            if (pawn?.genes == null) return string.Empty;
            EnsureDefinitionsLoaded();
            string xenotypeName = pawn.genes.xenotypeName ?? pawn.genes.Xenotype?.label ?? "baseline human";
            bool isCustom = pawn.genes.Xenotype == XenotypeDefOf.Baseliner && !string.IsNullOrEmpty(pawn.genes.xenotypeName);
            string xenotypeLabel = isCustom ? $"{xenotypeName} (custom hybrid)" : xenotypeName;

            var sb = new StringBuilder();
            sb.Append($"*Xenotype:* {xenotypeLabel}");

            if (pawn.genes.Xenotype != null && pawn.genes.Xenotype != XenotypeDefOf.Baseliner)
            {
                string xenotypeDesc = GetXenotypeDescription(pawn.genes.Xenotype);
                if (!string.IsNullOrEmpty(xenotypeDesc))
                {
                    sb.AppendLine();
                    sb.Append($"*What you are:* {xenotypeDesc}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Bag 1: Basic Appearance (Hair colors, skin, styles). Ideal for descriptions in group chat and external perception.
        /// </summary>
        public static List<Gene> GetBasicAppearanceGenes(List<Gene> activeGenes)
        {
            return activeGenes
                .Where(g => !IsIgnoredGene(g))
                .Where(g =>
                    // CONDITION A: XML priority. Assigned in XML as "BasicCosmetic", "BasicAppearance" or "Basic"
                    (CustomCategory.TryGetValue(g.def.defName, out var customCat) &&
                     (customCat.IndexOf("BasicCosmetic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      customCat.IndexOf("BasicAppearance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      customCat.Equals("Basic", StringComparison.OrdinalIgnoreCase)))
                    ||
                    // CONDITION B: Game category in the basic list
                    (g.def.displayCategory != null && BasicAppearanceCategories.Contains(g.def.displayCategory.defName))
                    ||
                    // CONDITION C: Keywords in defName
                    BasicAppearanceKeywords.Any(kw => g.def.defName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>
        /// Bag 2: Complex Physical Appearance / Mutations (Wings, horns, scales).
        /// </summary>
        public static List<Gene> GetMutationAppearanceGenes(List<Gene> activeGenes, List<Gene> avoidGenes = null)
        {
            return activeGenes
                .Where(g => !IsIgnoredGene(g))
                .Where(g =>
                    // CONDITION A: XML Priority. If we have assigned the category "Cosmetic" or "Mutation" in our XML (excluding BasicCosmetic)
                    (CustomCategory.TryGetValue(g.def.defName, out var customCat) &&
                     (customCat.IndexOf("Cosmetic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      customCat.IndexOf("Mutation", StringComparison.OrdinalIgnoreCase) >= 0) &&
                     customCat.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) < 0)
                    ||
                    // CONDITION B: Its category in the game contains "Cosmetic"
                    (g.def.displayCategory != null && g.def.displayCategory.defName.Contains("Cosmetic"))
                    ||
                    // CONDITION C: The native defName contains obvious physical keywords
                    MutationAppearanceKeywords.Any(kw => g.def.defName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    g.def.defName.EndsWith("Ears", StringComparison.OrdinalIgnoreCase) ||
                    g.def.defName.EndsWith("Tails", StringComparison.OrdinalIgnoreCase) ||
                    g.def.defName.EndsWith("Wings", StringComparison.OrdinalIgnoreCase) ||
                    g.def.defName.EndsWith("Horns", StringComparison.OrdinalIgnoreCase)
                    ||
                    // CONDITION D: The native category contains obvious physical keywords
                    (g.def.displayCategory != null && MutationAppearanceKeywords.Any(kw => g.def.displayCategory.defName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0))
                )
                // Exclude standard human reference genes (ears/hands/nose)
                .Where(g => !IsBaselineHumanGene(g.def))
                // Exclude false positives containing substrings like 'ear' (e.g., Learning_*, Nearsighted)
                .Where(g => !g.def.defName.StartsWith("Learning_", StringComparison.OrdinalIgnoreCase) &&
                            !g.def.defName.Equals("Nearsighted", StringComparison.OrdinalIgnoreCase))
                // Exclude if explicitly forced as Ability or Trait in the XML
                .Where(g => !(CustomCategory.TryGetValue(g.def.defName, out var cat) &&
                             (cat.IndexOf("Ability", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              cat.IndexOf("Trait", StringComparison.OrdinalIgnoreCase) >= 0)))
                // We exclude genes for absence/negation (such as 'no wings', 'no antennae')
                .Where(g => !g.def.defName.StartsWith("AG_No", StringComparison.OrdinalIgnoreCase) && !(g.def.label != null && g.def.label.StartsWith("no ", StringComparison.OrdinalIgnoreCase)))
                // We securely subtract the exclusion list (such as hair/skin colors)
                .Except(avoidGenes ?? Enumerable.Empty<Gene>())
                .ToList();
        }

        /// <summary>
        /// Set 3: Genes for mandatory needs and fixed biological dependencies (Hemogen, chemical dependencies, deathrest, or others enforced via XML using the `customCategory="Needs"` attribute).
        /// </summary>
        public static List<Gene> GetSpecialNeedsGenes(List<Gene> activeGenes)
        {
            if (activeGenes == null || !activeGenes.Any()) return new List<Gene>();

            return activeGenes.Where(gene =>
                gene?.def != null &&
                !IsIgnoredGene(gene) &&
                (
                    // CONDITION A: Explicitly forced in the XML with the category "Needs" or "Need"
                    (CustomCategory.TryGetValue(gene.def.defName, out var customCat) &&
                     customCat.IndexOf("Need", StringComparison.OrdinalIgnoreCase) >= 0)
                    ||
                    // CONDITION B: Native fallback
                    gene is Gene_ChemicalDependency ||
                    (gene.def.geneClass != null && typeof(Gene_ChemicalDependency).IsAssignableFrom(gene.def.geneClass)) ||
                    gene.def.defName.StartsWith("ChemicalDependency_", StringComparison.OrdinalIgnoreCase) ||
                    gene is Gene_HemogenDrain ||
                    gene.def.defName.Equals("HemogenDrain", StringComparison.OrdinalIgnoreCase) ||
                    (gene.def.defName.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0 && gene.def.resourceLossPerDay > 0) ||
                    gene is Gene_Deathrest ||
                    gene.def.defName.Equals("Deathrest", StringComparison.OrdinalIgnoreCase)
                )
            ).ToList();
        }

        /// <summary>
        /// Detects the permanent biological requirements imposed by genes (Hemogen, chemical dependencies, deathrest, etc.).
        /// Prioritizes the description provided in the XML (promptDescription); if it doesn't exist, uses the native fallback descriptions.
        /// (Note: Withdrawal or acute health crises are managed in the Health section via Hediffs).
        /// </summary>
        public static List<string> GetGeneticSpecialNeeds(List<Gene> activeGenes)
        {
            if (activeGenes == null || !activeGenes.Any()) return new List<string>();

            var needs = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unoverriddenChemicals = new List<string>();
            var needsGenes = GetSpecialNeedsGenes(activeGenes);

            foreach (var gene in needsGenes)
            {
                if (gene?.def == null) continue;

                string defName = gene.def.defName;

                // 1. PRIORITY XML: If a custom description is specified in the XML, we use it directly
                if (CustomGeneDescriptions.TryGetValue(defName, out var customDesc) && !string.IsNullOrWhiteSpace(customDesc))
                {
                    if (seen.Add(customDesc.Trim()))
                        needs.Add(customDesc.Trim());
                    continue;
                }

                // 2. FALLBACK: If there is no description in the XML, we apply the known native fallbacks
                // A) Chemical dependencies (Psite, Go-Juice, Wake-up, Alcohol, etc.)
                if (gene is Gene_ChemicalDependency chemDep && chemDep.def?.chemical != null)
                {
                    unoverriddenChemicals.Add(chemDep.def.chemical.label.ToLowerInvariant());
                }
                else if (defName.StartsWith("ChemicalDependency_", StringComparison.OrdinalIgnoreCase))
                {
                    string drug = defName.Substring("ChemicalDependency_".Length).ToLowerInvariant();
                    unoverriddenChemicals.Add(drug);
                }
                // B) Biological need for hemogen / human blood
                else if (gene is Gene_HemogenDrain ||
                         defName.Equals("HemogenDrain", StringComparison.OrdinalIgnoreCase) ||
                         (defName.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0 && gene.def.resourceLossPerDay > 0))
                {
                    const string hemogenFallback = "requires regular consumption of human blood (hemogen) to survive";
                    if (seen.Add(hemogenFallback))
                        needs.Add(hemogenFallback);
                }
                // C) Requirement for deathrest (Deathrest)
                else if (gene is Gene_Deathrest || defName.Equals("Deathrest", StringComparison.OrdinalIgnoreCase))
                {
                    const string deathrestFallback = "requires periodic deathrest comas to regenerate archite cells";
                    if (seen.Add(deathrestFallback))
                        needs.Add(deathrestFallback);
                }
                // D) Other genes labeled with "Needs" in the XML that lack a promptDescription
                else
                {
                    string formatted = FormatGeneForPrompt(gene);
                    if (!string.IsNullOrEmpty(formatted) && seen.Add(formatted))
                        needs.Add(formatted);
                }
            }

            // Group the chemical dependencies that didn't have individual overrides in the XML
            if (unoverriddenChemicals.Any())
            {
                var uniqueChems = unoverriddenChemicals.Distinct().ToList();
                needs.Add($"biochemically dependent on regular intake of {string.Join(", ", uniqueChems)} (cannot synthesize vital neurotransmitters without it)");
            }

            return needs;
        }

        public static List<string> GetGeneticSpecialNeeds(Pawn pawn)
        {
            if (pawn?.genes == null) return new List<string>();
            return GetGeneticSpecialNeeds(GetActiveGenes(pawn));
        }

        /// <summary>
        /// Generate the formatted text regarding genetic needs, ready to be inserted into the prompt.
        /// </summary>
        public static string BuildSpecialNeedsPrompt(List<Gene> activeGenes)
        {
            var needs = GetGeneticSpecialNeeds(activeGenes);
            return needs.Any() ? string.Join("; ", needs) : string.Empty;
        }

        public static string BuildSpecialNeedsPrompt(Pawn pawn)
        {
            if (pawn?.genes == null) return string.Empty;
            return BuildSpecialNeedsPrompt(GetActiveGenes(pawn));
        }

        /// <summary>
        /// Bag 4: Traits and Abilities (excluding appearance and special needs).
        /// </summary>
        public static List<Gene> GetTraitAndAbilityGenesList(List<Gene> validGenes, List<Gene> avoidGenes = null)
        {
            return validGenes
                .Except(avoidGenes ?? Enumerable.Empty<Gene>())
                .Where(g => !IsIgnoredGene(g))
                .Where(g => !IsBaselineHumanGene(g.def))
                .Where(g =>
                    // CONDITION A: If we have forced it as a "Trait" or "Ability" via the XML, it passes the filter directly.
                    (CustomCategory.TryGetValue(g.def.defName, out var customCat) &&
                    (customCat.IndexOf("Trait", StringComparison.OrdinalIgnoreCase) >= 0 || customCat.IndexOf("Ability", StringComparison.OrdinalIgnoreCase) >= 0))
                    ||
                    // CONDITION B: If it grants active abilities (Bloodfeed, Coagulate, Resurrect, etc.), it ALWAYS passes.
                    (g.def.abilities != null && g.def.abilities.Any())
                    ||
                    // CONDITION C: If it is NOT in the exclusion list and is not native mechanical garbage
                    (
                        (g.def.displayCategory == null || !MechanicalCategories.Contains(g.def.displayCategory.defName))
                        &&
                        // Purging of genes that only confer native traits (since these are already conveyed via the psychological profile)
                        (g.def.forcedTraits == null || !g.def.forcedTraits.Any())
                        &&
                        // Purging of genes that already have a behavioral directive (unless they grant active abilities)
                        (!HasBehavioralDirective(g) || (g.def.abilities != null && g.def.abilities.Any()))
                    )
                )
                .ToList();
        }

        /// <summary>
        /// Formats a gene for a bullet list: "Label: Description." or simply "Label" if there is no description.
        /// </summary>
        public static string FormatGeneForBulletList(Gene gene)
        {
            if (gene?.def == null || IsIgnoredGene(gene)) return string.Empty;

            var info = ResolveGeneDisplay(gene, true, 90);
            if (string.IsNullOrEmpty(info.Label)) return string.Empty;
            string label = info.Label.CapitalizeFirst();

            if (info.Description != null)
            {
                string desc = info.Description.TrimEnd('.');
                return $"{label}: {desc}.";
            }

            return label;
        }

        /// <summary>
        /// Extracts all behavioral directives from the pawn's active genes,
        /// formatted with the gene name and concept header: "GeneName (Description): Directive".
        /// </summary>
        public static List<string> GetBehavioralDirectives(Pawn pawn)
        {
            if (pawn?.genes == null) return new List<string>();
            return GetBehavioralDirectives(GetActiveGenes(pawn));
        }

        /// <summary>
        /// Extracts all behavioral directives from a given list of active genes,
        /// formatted with the gene name and concept header: "GeneName (Description): Directive".
        /// </summary>
        public static List<string> GetBehavioralDirectives(List<Gene> activeGenes)
        {
            if (activeGenes == null || !activeGenes.Any()) return new List<string>();

            var directives = new List<string>();
            foreach (var gene in activeGenes)
            {
                if (gene?.def == null || IsIgnoredGene(gene)) continue;

                if (BehavioralDirectives.TryGetValue(gene.def.defName, out var directive) && !string.IsNullOrWhiteSpace(directive))
                {
                    directives.Add(directive.Trim());
                }
            }

            return directives;
        }

        /// <summary>
        /// Generates the complete markdown block for Genetic Behavioral Directives, ready to inject into any prompt context.
        /// </summary>
        public static string BuildBehavioralDirectivesPrompt(Pawn pawn)
        {
            if (pawn?.genes == null) return string.Empty;
            return BuildBehavioralDirectivesPrompt(GetActiveGenes(pawn));
        }


        public static string BuildGroupChatVisualDescription(List<Gene> activeGenes)
        {
            var basicApp = GetBasicAppearanceGenes(activeGenes);
            var mutApp = GetMutationAppearanceGenes(activeGenes, basicApp);

            var allVisuals = basicApp.Concat(mutApp).ToList();

            if (!allVisuals.Any()) return string.Empty;

            var visualNames = allVisuals.Select(g => FormatGeneForPrompt(g, false).Trim().ToLower()).ToList();

            return $"{string.Join(", ", visualNames)}.*";
        }

        /// <summary>
        /// Builds the visual/physical section of a colonist's genes. 
        /// If any of the genes contain a detailed description in XML, it is formatted as a bulleted list. 
        /// If they are merely simple tags, they are formatted as a single, compact sentence.
        /// </summary>
        public static string BuildVisualAppearancePrompt(Pawn pawn, List<Gene> mutationGenes)
        {
            return BuildVisualAppearancePrompt(pawn?.LabelShort ?? "The colonist", mutationGenes);
        }

        /// <summary>
        /// Construct the visual/physical section of genes, given the colonist's name and their list of mutations.
        /// </summary>
        public static string BuildVisualAppearancePrompt(string pawnName, List<Gene> mutationGenes)
        {
            if (mutationGenes == null || !mutationGenes.Any()) return string.Empty;

            var formattedItems = new List<string>();
            bool hasDetailedNotes = false;

            foreach (var gene in mutationGenes)
            {
                if (gene?.def == null) continue;

                var info = ResolveGeneDisplay(gene, false);
                if (string.IsNullOrEmpty(info.Label)) continue;

                if (info.Description != null)
                {
                    hasDetailedNotes = true;
                    formattedItems.Add($"{info.Label.CapitalizeFirst()}: {info.Description.TrimEnd('.')}.");
                }
                else
                {
                    formattedItems.Add(info.Label);
                }
            }

            if (!formattedItems.Any()) return string.Empty;

            var sb = new StringBuilder();
            if (hasDetailedNotes)
            {
                sb.AppendLine($"*Visually, {pawnName}'s body features:*");
                foreach (var item in formattedItems)
                {
                    string line = item.EndsWith(".") ? item : $"{item.CapitalizeFirst()}.";
                    sb.AppendLine($"  - {line}");
                }
            }
            else
            {
                sb.Append($"*Visually, {pawnName}'s body features:* {string.Join(", ", formattedItems)}.");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Returns the readable xenotype of the colonist (e.g., "Yttakin", "Dirtmole", or "Baseliner").
        /// </summary>
        public static string GetPawnXenotypeLabel(Pawn pawn)
        {
            if (pawn == null) return "Baseliner";
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                if (!string.IsNullOrEmpty(pawn.genes.xenotypeName))
                    return pawn.genes.xenotypeName;
                if (pawn.genes.Xenotype != null && pawn.genes.Xenotype != XenotypeDefOf.Baseliner)
                    return pawn.genes.Xenotype.LabelCap;
            }
            return "Baseliner";
        }

        public static string GetGeneLabel(Gene gene)
        {
            if (gene?.def == null) return string.Empty;
            EnsureDefinitionsLoaded();
            return LabelOverrides.TryGetValue(gene.def.defName, out var customLabel) ? customLabel : gene.def.label;
        }

        /// <summary>
        /// Generates the complete markdown block for Genetic Behavioral Directives from active genes.
        /// </summary>
        public static string BuildBehavioralDirectivesPrompt(List<Gene> activeGenes)
        {
            var directives = GetBehavioralDirectives(activeGenes);
            if (!directives.Any()) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("*Genetic Behavioral Directives:*");
            foreach (var dir in directives)
            {
                sb.AppendLine($"  - {dir}");
            }

            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────
        // PROMPT BUILDER: Genetics section
        // ─────────────────────────────────────────

        public static string BuildGeneticsInfo(Pawn pawn)
        {
            if (pawn.genes == null) return "";
            EnsureDefinitionsLoaded();

            var sb = new StringBuilder();

            sb.AppendLine(GetXenotypeInfo(pawn));

            var activeGenes = GetActiveGenes(pawn);

            if (!activeGenes.Any())
            {
                sb.AppendLine();
                return sb.ToString();
            }

            // ── Bag 1 & 2: Appearance ────────────────────────────────────────────
            List<Gene> basicAppearanceGenes = GetBasicAppearanceGenes(activeGenes);
            List<Gene> mutationAppearanceGenes = GetMutationAppearanceGenes(activeGenes, basicAppearanceGenes);

            string visualPrompt = BuildVisualAppearancePrompt(pawn, mutationAppearanceGenes);
            if (!string.IsNullOrEmpty(visualPrompt))
            {
                sb.AppendLine();
                sb.AppendLine(visualPrompt);
            }

            // ── Bag 3: Special biological needs & dependencies ───────────────────
            List<Gene> specialNeedsGenes = GetSpecialNeedsGenes(activeGenes);
            var needsList = GetGeneticSpecialNeeds(activeGenes);

            // ── Bag 4: Functional traits & abilities (excluding visuals & needs) ──
            var excludedGenes = basicAppearanceGenes
                .Concat(mutationAppearanceGenes)
                .Concat(specialNeedsGenes)
                .ToList();
            List<Gene> functionalGenes = GetTraitAndAbilityGenesList(activeGenes, excludedGenes);

            // Split: genes that grant active abilities vs passive genetic traits
            var abilityGenes = functionalGenes
                .Where(g => g.def.abilities != null && g.def.abilities.Any())
                .Select(g => FormatGeneForBulletList(g))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var traitGenes = functionalGenes
                .Where(g => g.def.abilities == null || !g.def.abilities.Any())
                .Select(g => FormatGeneForBulletList(g))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (traitGenes.Any())
            {
                sb.AppendLine();
                sb.AppendLine("*Genetic traits:*");
                foreach (var trait in traitGenes)
                {
                    sb.AppendLine($"  - {trait}");
                }
            }

            if (abilityGenes.Any())
            {
                sb.AppendLine();
                sb.AppendLine("*Genetic abilities (you can actively use these):*");
                foreach (var ability in abilityGenes)
                {
                    sb.AppendLine($"  - {ability}");
                }
            }

            if (needsList.Any())
            {
                sb.AppendLine();
                if (needsList.Count > 1)
                {
                    sb.AppendLine("*Genetic needs:*");
                    foreach (var need in needsList)
                    {
                        sb.AppendLine($"  - {need.CapitalizeFirst().TrimEnd('.')}.");
                    }
                }
                else
                {
                    sb.AppendLine($"*Genetic needs:* {needsList.First().CapitalizeFirst().TrimEnd('.')}.");
                }
            }

            // ── Behavioral Directives (Psychology, Instincts & Mortality) ───────
            string directivesPrompt = BuildBehavioralDirectivesPrompt(activeGenes);
            if (!string.IsNullOrEmpty(directivesPrompt))
            {
                sb.AppendLine();
                sb.AppendLine(directivesPrompt);
            }

            sb.AppendLine();
            sb.Append("*Important:* You are fully aware of your nature and genetics. Speak about your physical traits, abilities, and needs naturally as part of who you are.");
            sb.AppendLine();

            return sb.ToString();
        }



        /// <summary>
        /// Exports a detailed report in .txt format of all genes loaded into the game. 
        /// The export logic is delegated to GeneClassificationDebugExporter to keep this class clean.
        /// </summary>
        public static void ExportAllGenesClassificationDebug()
            => GeneClassificationDebugExporter.Export(CustomCategory, BehavioralDirectives);
    }
}
