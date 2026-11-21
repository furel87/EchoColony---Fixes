using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace EchoColony
{
    /// <summary>
    /// Standalone debug exporter extracted from GenePromptClassifier.
    /// Exports a detailed .txt report of all loaded genes classified by prompt category.
    /// </summary>
    public static class GeneClassificationDebugExporter
    {
        /// <summary>
        /// Exports a detailed .txt report of all genes loaded in the game,
        /// classifying them according to the filters designed for LLM prompts.
        /// </summary>
        public static void Export(
            Dictionary<string, string> customCategory,
            Dictionary<string, string> behavioralDirectives)
        {
            try
            {
                GenePromptClassifier.EnsureDefinitionsLoaded();

                if (!ModsConfig.BiotechActive)
                {
                    Messages.Message("EchoColony.BiotechRequired".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                var allGeneDefs = DefDatabase<GeneDef>.AllDefsListForReading;
                if (allGeneDefs == null || !allGeneDefs.Any())
                {
                    Messages.Message("No genes found in DefDatabase.", MessageTypeDefOf.RejectInput);
                    return;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string folderPath  = Path.Combine(desktopPath, "EchoColony_Debug");
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string filePath = Path.Combine(folderPath, "EchoColony_Genes_Classification_Debug.txt");

                var dummyGenes = new List<Gene>();
                foreach (var def in allGeneDefs.OrderBy(g => g.defName))
                {
                    Gene dummy;
                    try
                    {
                        if (def.geneClass != null && typeof(Gene).IsAssignableFrom(def.geneClass))
                        {
                            dummy = (Gene)Activator.CreateInstance(def.geneClass);
                        }
                        else
                        {
                            dummy = new Gene();
                        }
                    }
                    catch
                    {
                        dummy = new Gene();
                    }
                    dummy.def = def;
                    dummyGenes.Add(dummy);
                }

                // Bucket 1: Basic appearance
                var basicAppearanceGenes = GenePromptClassifier.GetBasicAppearanceGenes(dummyGenes);

                // Bucket 2: Appearance mutations
                var mutationAppearanceGenes = GenePromptClassifier.GetMutationAppearanceGenes(dummyGenes, basicAppearanceGenes);

                // Bucket 3: Specific needs
                var specialNeedsGenes = GenePromptClassifier.GetSpecialNeedsGenes(dummyGenes);

                // Bucket 4: Functional abilities and traits
                var appearanceAndNeeds = basicAppearanceGenes
                    .Concat(mutationAppearanceGenes)
                    .Concat(specialNeedsGenes)
                    .ToList();

                var functionalGenes = GenePromptClassifier.GetTraitAndAbilityGenesList(dummyGenes, appearanceAndNeeds);

                var abilityGenes = functionalGenes
                    .Where(g => g.def.abilities != null && g.def.abilities.Any())
                    .ToList();

                var traitGenes = functionalGenes
                    .Where(g => g.def.abilities == null || !g.def.abilities.Any())
                    .ToList();

                // Candidate genes for traits/abilities evaluated
                var traitCandidates = dummyGenes.Except(appearanceAndNeeds).ToList();

                // Genes excluded from traits due to associated personality traits (forcedTraits)
                var genesRejectedForTraits = traitCandidates
                    .Where(g => g.def.forcedTraits != null && g.def.forcedTraits.Any())
                    .ToList();

                // Genes with behavioral directives (routed to Behavioral Directives to prevent duplication)
                var genesHandledByDirectives = traitCandidates
                    .Where(g => GenePromptClassifier.HasBehavioralDirective(g) && (g.def.abilities == null || !g.def.abilities.Any()))
                    .ToList();

                // Genes excluded from traits due to mechanical categories
                var mechanicalCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Reproduction", "Archite", "Metabolism", "Hemogen", "Pain", "Healing", "Misc", "Aptitudes", "BS_DO_NOT", "BS_Pilotable", "BS_Production"
                };

                var genesRejectedMechanical = traitCandidates
                    .Except(functionalGenes)
                    .Except(genesRejectedForTraits)
                    .Except(genesHandledByDirectives)
                    .Where(g => g.def.displayCategory != null && mechanicalCategories.Contains(g.def.displayCategory.defName))
                    .ToList();

                // Genes with registered behavioral directives
                var directiveGenes = dummyGenes
                    .Where(g => GenePromptClassifier.HasBehavioralDirective(g))
                    .ToList();

                // Physical negation genes ("no ...", "AG_No...")
                var negationGenes = dummyGenes
                    .Where(g => g.def.defName.StartsWith("AG_No", StringComparison.OrdinalIgnoreCase) ||
                               (g.def.label != null && g.def.label.StartsWith("no ", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // Genes included in at least one prompt
                var promptIncludedGenes = basicAppearanceGenes
                    .Concat(mutationAppearanceGenes)
                    .Concat(specialNeedsGenes)
                    .Concat(abilityGenes)
                    .Concat(traitGenes)
                    .Concat(directiveGenes)
                    .Distinct()
                    .ToList();

                // Genes entirely excluded from prompts
                var totallyRejectedGenes = dummyGenes.Except(promptIncludedGenes).ToList();

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("ECHOCOLONY - GENE CLASSIFICATION FOR LLM PROMPTS");
                sb.AppendLine("================================================================================");
                sb.AppendLine($"Generation date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Total GeneDefs loaded in the game: {dummyGenes.Count}");
                sb.AppendLine($"Biotech active: {ModsConfig.BiotechActive}");
                sb.AppendLine($"AlphaGenes loaded: {ModsConfig.IsActive("sarg.alphagenes")}");
                sb.AppendLine($"Big & Small loaded: {ModsConfig.IsActive("redmattis.bigsmall.core")}");
                sb.AppendLine();
                sb.AppendLine("This report reflects exactly which genes pass into each prompt category");
                sb.AppendLine("when a colonist has those genes active, and the reasons why they are filtered.");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                // 1. APPEARANCE AND COSMETICS
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║ 1. APPEARANCE AND COSMETIC GENES (Total: {basicAppearanceGenes.Count + mutationAppearanceGenes.Count})");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine();
                sb.AppendLine($"─── 1.1 Basic Cosmetics (Skin/hair colors, styles) ({basicAppearanceGenes.Count}) ───");
                sb.AppendLine("Used for brief group descriptions and general appearance.");
                foreach (var g in basicAppearanceGenes.OrderBy(x => x.def.defName))
                {
                    string cat = g.def.displayCategory?.defName ?? "NoCategory";
                    string formatted = GenePromptClassifier.FormatGeneForPrompt(g, false);
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat}) -> Prompt: {formatted}");
                }
                sb.AppendLine();

                sb.AppendLine($"─── 1.2 Physical Mutations and Complex Cosmetics ({mutationAppearanceGenes.Count}) ───");
                sb.AppendLine("Injected into: *Visually, [Name]'s body features: ...*");
                sb.AppendLine("(If they contain a detailed description in XML, they are structured as a bulleted list; if simple, as a single sentence)");
                foreach (var g in mutationAppearanceGenes.OrderBy(x => x.def.defName))
                {
                    string cat = g.def.displayCategory?.defName ?? "NoCategory";
                    string formatted = GenePromptClassifier.FormatGeneForPrompt(g, false);
                    string customNote = customCategory.ContainsKey(g.def.defName) ? " [XML: CustomCategory=Cosmetic]" : "";
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat}){customNote} -> Prompt: {formatted}");
                }
                sb.AppendLine();

                // 2. BIOLOGICAL NEEDS
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║ 2. BIOLOGICAL NEEDS AND DEPENDENCY GENES ({specialNeedsGenes.Count})");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine("Injected into: *Genetic needs: ...*");
                foreach (var g in specialNeedsGenes.OrderBy(x => x.def.defName))
                {
                    string cat = g.def.displayCategory?.defName ?? "NoCategory";
                    var singleNeed = GenePromptClassifier.GetGeneticSpecialNeeds(new List<Gene> { g });
                    string needDesc = singleNeed.Any() ? string.Join("; ", singleNeed) : "(no description)";
                    string origin = customCategory.ContainsKey(g.def.defName) ? "[XML: CustomCategory=Needs]" : "[C# Detection]";
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat}) {origin}");
                    sb.AppendLine($"      Injection: {needDesc}");
                }
                sb.AppendLine();

                // 3. FUNCTIONAL ABILITIES AND TRAITS
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║ 3. FUNCTIONAL ABILITY AND TRAIT GENES (Total valid: {functionalGenes.Count})");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine();
                sb.AppendLine($"─── 3.1 Active Abilities ({abilityGenes.Count}) ───");
                sb.AppendLine("Injected into: *Genetic abilities (you can actively use these): ...*");
                foreach (var g in abilityGenes.OrderBy(x => x.def.defName))
                {
                    string abilitiesList = string.Join(", ", g.def.abilities.Select(a => a.defName));
                    string formatted = GenePromptClassifier.FormatGeneForPrompt(g);
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Abilities: {abilitiesList})");
                    sb.AppendLine($"      Injection: {formatted}");
                }
                sb.AppendLine();

                sb.AppendLine($"─── 3.2 Passive Genetic Traits ({traitGenes.Count}) ───");
                sb.AppendLine("Injected into: *Genetic traits: ...*");
                foreach (var g in traitGenes.OrderBy(x => x.def.defName))
                {
                    string cat = g.def.displayCategory?.defName ?? "NoCategory";
                    string formatted = GenePromptClassifier.FormatGeneForPrompt(g);
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat})");
                    sb.AppendLine($"      Injection: {formatted}");
                }
                sb.AppendLine();

                sb.AppendLine($"─── 3.3 Genes Excluded from Traits for granting a personality 'Trait' ({genesRejectedForTraits.Count}) ───");
                sb.AppendLine("These genes are NOT injected as a genetic trait because their effect is already conveyed to the LLM");
                sb.AppendLine("through the psychological profile and the colonist's personality traits (TraitDef) list:");
                foreach (var g in genesRejectedForTraits.OrderBy(x => x.def.defName))
                {
                    string traitsList = string.Join(", ", g.def.forcedTraits.Select(t => t.def?.defName ?? "null"));
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" -> Grants trait(s): [{traitsList}]");
                }
                sb.AppendLine();

                sb.AppendLine($"─── 3.4 Trait Genes routed as Behavioral Directives ({genesHandledByDirectives.Count}) ───");
                sb.AppendLine("These genes possess explicit behavioral instructions and are injected into Section 4");
                sb.AppendLine("(Behavioral Directives) to provide the colonist with a detailed psychological role without duplication:");
                foreach (var g in genesHandledByDirectives.OrderBy(x => x.def.defName))
                {
                    string cat = g.def.displayCategory?.defName ?? "NoCategory";
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat}) -> See Section 4 (Behavioral Directive)");
                }
                sb.AppendLine();

                // 4. BEHAVIORAL DIRECTIVES
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║ 4. GENE-BASED BEHAVIORAL DIRECTIVES ({directiveGenes.Count})");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine("Injected into: *Genetic Behavioral Directives:*");
                foreach (var g in directiveGenes.OrderBy(x => x.def.defName))
                {
                    string directive = behavioralDirectives[g.def.defName];
                    string header = GenePromptClassifier.FormatGeneForPrompt(g);

                    var otherSacks = new List<string>();
                    if (basicAppearanceGenes.Contains(g)) otherSacks.Add("Basic Cosmetics");
                    if (mutationAppearanceGenes.Contains(g)) otherSacks.Add("Appearance Mutation");
                    if (specialNeedsGenes.Contains(g)) otherSacks.Add("Needs");
                    if (abilityGenes.Contains(g)) otherSacks.Add("Active Ability");
                    if (traitGenes.Contains(g)) otherSacks.Add("Passive Trait");
                    if (genesRejectedForTraits.Contains(g)) otherSacks.Add("Excluded from Traits (Trait)");

                    string otherInfo = otherSacks.Any() ? $" [Also present in: {string.Join(", ", otherSacks)}]" : " [Pure Directive / Role]";
                    sb.AppendLine($"  • [{g.def.defName}] \"{g.def.label}\"{otherInfo}");
                    sb.AppendLine($"      Prompt: {header}: {directive}");
                }
                sb.AppendLine();

                // 5. REJECTED / EXCLUDED GENES
                sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                sb.AppendLine($"║ 5. REJECTED / EXCLUDED GENES FROM PROMPTS ({totallyRejectedGenes.Count})");
                sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                sb.AppendLine("These genes do not generate text in the colonist's prompt:");
                sb.AppendLine();

                // Subgroup 5.1: Standard human baseline (normal ears, jaw, hands)
                var rejectedBaseline = totallyRejectedGenes.Where(g => GenePromptClassifier.IsBaselineHumanGene(g.def)).ToList();
                if (rejectedBaseline.Any())
                {
                    sb.AppendLine($"  [Standard human anatomy genes (discarded by default)] ({rejectedBaseline.Count}):");
                    sb.AppendLine("  (Omitted to avoid describing basic human anatomy as a mutation or trait)");
                    foreach (var g in rejectedBaseline.OrderBy(x => x.def.defName))
                    {
                        sb.AppendLine($"    • [{g.def.defName}] \"{g.def.label}\"");
                    }
                    sb.AppendLine();
                }

                // Subgroup 5.2: Negation
                var rejectedNegation = totallyRejectedGenes.Intersect(negationGenes).Except(rejectedBaseline).ToList();
                if (rejectedNegation.Any())
                {
                    sb.AppendLine($"  [Anatomical negation genes ('no wings', 'no antennae', etc.)] ({rejectedNegation.Count}):");
                    foreach (var g in rejectedNegation.OrderBy(x => x.def.defName))
                    {
                        sb.AppendLine($"    • [{g.def.defName}] \"{g.def.label}\"");
                    }
                    sb.AppendLine();
                }

                // Subgroup 5.3: Excluded due to Trait (and without additional directive)
                var rejectedByTraitOnly = totallyRejectedGenes.Intersect(genesRejectedForTraits).Except(rejectedBaseline).ToList();
                if (rejectedByTraitOnly.Any())
                {
                    sb.AppendLine($"  [Excluded for granting psychological Trait and without additional directive] ({rejectedByTraitOnly.Count}):");
                    foreach (var g in rejectedByTraitOnly.OrderBy(x => x.def.defName))
                    {
                        string traitsList = string.Join(", ", g.def.forcedTraits.Select(t => t.def?.defName ?? "null"));
                        sb.AppendLine($"    • [{g.def.defName}] \"{g.def.label}\" -> Trait: [{traitsList}]");
                    }
                    sb.AppendLine();
                }

                // Subgroup 5.4: Mechanical categories
                var rejectedMechanical = totallyRejectedGenes.Intersect(genesRejectedMechanical).Except(rejectedBaseline).ToList();
                if (rejectedMechanical.Any())
                {
                    sb.AppendLine($"  [Excluded due to purely mechanical/statistical category] ({rejectedMechanical.Count}):");
                    foreach (var g in rejectedMechanical.OrderBy(x => x.def.defName))
                    {
                        string cat = g.def.displayCategory?.defName ?? "NoCategory";
                        sb.AppendLine($"    • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat})");
                    }
                    sb.AppendLine();
                }

                // Subgroup 5.5: Other
                var remainingRejected = totallyRejectedGenes
                    .Except(rejectedBaseline)
                    .Except(rejectedNegation)
                    .Except(rejectedByTraitOnly)
                    .Except(rejectedMechanical)
                    .ToList();

                if (remainingRejected.Any())
                {
                    sb.AppendLine($"  [Other unclassified genes / without effect for prompts] ({remainingRejected.Count}):");
                    foreach (var g in remainingRejected.OrderBy(x => x.def.defName))
                    {
                        string cat = g.def.displayCategory?.defName ?? "NoCategory";
                        sb.AppendLine($"    • [{g.def.defName}] \"{g.def.label}\" (Cat: {cat})");
                    }
                    sb.AppendLine();
                }

                // STATISTICAL SUMMARY
                sb.AppendLine("================================================================================");
                sb.AppendLine("CLASSIFICATION STATISTICAL SUMMARY");
                sb.AppendLine("================================================================================");
                sb.AppendLine($"  • Total GeneDefs evaluated:                     {dummyGenes.Count}");
                sb.AppendLine($"  • Basic Cosmetics:                              {basicAppearanceGenes.Count}");
                sb.AppendLine($"  • Cosmetics and Physical Mutations:             {mutationAppearanceGenes.Count}");
                sb.AppendLine($"  • Specific Biological Needs:                    {specialNeedsGenes.Count}");
                sb.AppendLine($"  • Active Abilities:                             {abilityGenes.Count}");
                sb.AppendLine($"  • Passive Traits:                               {traitGenes.Count}");
                sb.AppendLine($"  • Excluded from traits due to associated Trait: {genesRejectedForTraits.Count}");
                sb.AppendLine($"  • Redirected to Behavioral Directives:          {genesHandledByDirectives.Count}");
                sb.AppendLine($"  • Total with Behavioral Directive:              {directiveGenes.Count}");
                sb.AppendLine($"  • Standard human anatomy (discarded):           {rejectedBaseline.Count}");
                sb.AppendLine($"  • Totally Rejected / Without Prompt:            {totallyRejectedGenes.Count}");
                sb.AppendLine("================================================================================");

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                Messages.Message("EchoColony.DebugGenesExported".Translate(filePath), MessageTypeDefOf.PositiveEvent);
                Log.Message($"[EchoColony] Gene classification debug exported to: {filePath}");
            }
            catch (Exception ex)
            {
                Messages.Message("EchoColony.DebugGenesWriteError".Translate(ex.Message), MessageTypeDefOf.RejectInput);
                Log.Error($"[EchoColony] Failed to export gene classification: {ex}");
            }
        }
    }
}
