using RimWorld;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace EchoColony
{
    public static class UtilsPromptHelpers
    {
        /// <summary>
        /// Builds a formatted string representing the traits of a given pawn, including their descriptions if available.
        /// </summary>
        /// <param name="pawn"></param>
        /// <returns></returns>
        public static string BuildTraits(Pawn pawn)
        {
            if (pawn.story?.traits == null || !pawn.story.traits.allTraits.Any())
                return "*Traits:* None";

            var entries = new List<string>();

            foreach (var t in pawn.story.traits.allTraits)
            {
                // 1. Obtener la descripción nativa traducida y formateada para el peón
                string rawDesc = t.CurrentData?.description ?? t.def?.description;
                string formattedDesc = "";

                if (!string.IsNullOrEmpty(rawDesc))
                {
                    formattedDesc = FormatText(rawDesc, pawn);
                }

                string label = t.LabelCap;

                if (!string.IsNullOrEmpty(formattedDesc))
                {
                    entries.Add($"  - {label}: \"{formattedDesc}\"");
                }
                else
                {
                    entries.Add($"  - {label}");
                }
            }

            return "*Traits & Core Identity:*\n" + string.Join("\n", entries);
        }

        /// <summary>
        /// Formats a raw text string by resolving RimWorld dynamic substitutions and removing internal UI tags and XML/HTML formatting.
        /// </summary>
        /// <param name="rawText"></param>
        /// <param name="pawn"></param>
        /// <returns></returns>
        private static string FormatText(string rawText, Pawn pawn)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

            try
            {
                //Resolves RimWorld dynamic substitutions. ([PAWN_nameDef], [PAWN_pronoun], etc.)
                string formatted = rawText.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).ToString();

                // 1. Elimina etiquetas internas de UI de RimWorld como (*Name)...(/Name)
                formatted = System.Text.RegularExpressions.Regex.Replace(formatted, @"\((\*|\/).*?\)", "");

                // 2. Elimina etiquetas de formato XML/HTML (<color=...>, <i>, etc.)
                formatted = System.Text.RegularExpressions.Regex.Replace(formatted, "<.*?>", "").Trim();

                return formatted;
            }
            catch
            {
                // Fallback defensivo si el formateador del juego falla con caracteres especiales
                string clean = System.Text.RegularExpressions.Regex.Replace(rawText, @"\((\*|\/).*?\)", "");
                return System.Text.RegularExpressions.Regex.Replace(clean, "<.*?>", "").Trim();
            }
        }

        /// <summary>
        /// Determina un nombre aproximado para el color de pelo/rasgos a partir del color RGB nativo.
        /// Utiliza el sistema de traducción Keyed de RimWorld para adaptarse al idioma activo.
        /// </summary>
        public static string GetApproximateColorName(UnityEngine.Color c)
        {
            float brightness = (0.299f * c.r) + (0.587f * c.g) + (0.114f * c.b);
            if (brightness > 0.85f && Math.Abs(c.r - c.g) < 0.12f && Math.Abs(c.g - c.b) < 0.12f)
                return "EchoColony.Color_WhiteGray".Translate().ToString();
            if (brightness < 0.18f)
                return "EchoColony.Color_Black".Translate().ToString();
            if (c.r > 0.55f && c.g < 0.35f && c.b < 0.25f)
                return "EchoColony.Color_Red".Translate().ToString();
            if (c.r > 0.60f && c.g > 0.50f && c.b < 0.40f)
                return "EchoColony.Color_Blonde".Translate().ToString();
            if (c.r > 0.25f && c.r > c.b && brightness < 0.55f)
                return "EchoColony.Color_Brown".Translate().ToString();
            if (brightness > 0.6f && Math.Abs(c.r - c.g) < 0.15f)
                return "EchoColony.Color_Gray".Translate().ToString();
            return "EchoColony.Color_Dark".Translate().ToString();
        }

        /// <summary>
        /// Obtiene la evaluación neta de atractivo/belleza de un peón (stat absoluto considerando genes, rasgos, implantes y desfiguración).
        /// Devuelve un texto cualitativo localizado según los archivos Keyed del mod o cadena vacía si es normal/promedio.
        /// </summary>
        public static string GetPawnAttractivenessSummary(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            try
            {
                float beautyStat = pawn.GetStatValue(StatDefOf.PawnBeauty);
                if (beautyStat >= 2.5f) return "EchoColony.Attractiveness_AngelicBeauty".Translate().ToString();
                if (beautyStat >= 1.5f) return "EchoColony.Attractiveness_ExceptionallyBeautiful".Translate().ToString();
                if (beautyStat >= 0.8f) return "EchoColony.Attractiveness_Attractive".Translate().ToString();
                if (beautyStat <= -2.5f) return "EchoColony.Attractiveness_Repulsive".Translate().ToString();
                if (beautyStat <= -1.5f) return "EchoColony.Attractiveness_Hideous".Translate().ToString();
                if (beautyStat <= -0.8f) return "EchoColony.Attractiveness_Unattractive".Translate().ToString();
            }
            catch
            {
                // Fallback defensivo si el stat no está disponible
            }

            return string.Empty;
        }

        /// <summary>
        /// Obtiene un resumen completo de los rasgos visuales y físicos morfológicos de un colono:
        /// tono de piel único (sin contradicciones entre exótico y melanina), pelo unificado (estilo + color, o calvo sin color),
        /// barba (solo si visible, omitiendo "imberbe"), complexión corporal y mutaciones físicas (alas, cuernos, cola, etc.).
        /// Totalmente genérico y compatible con cualquier idioma mediante defNames de RimWorld y cadenas Keyed.
        /// (Nota: La belleza se extrae de forma independiente en GetPawnAttractivenessSummary).
        /// </summary>
        public static string GetPawnAppearanceSummary(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            var traits = new List<string>();

            // 1. Genes Cosméticos y Mutaciones (Biotech / Alpha Genes / Big & Small)
            List<Gene> basicGenes = null;
            List<Gene> mutGenes = null;
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                GenePromptClassifier.EnsureDefinitionsLoaded();
                var activeGenes = GenePromptClassifier.GetActiveGenes(pawn);
                basicGenes = GenePromptClassifier.GetBasicAppearanceGenes(activeGenes);
                mutGenes = GenePromptClassifier.GetMutationAppearanceGenes(activeGenes, basicGenes);
            }
            basicGenes = basicGenes ?? new List<Gene>();
            mutGenes = mutGenes ?? new List<Gene>();

            // ==========================================
            // A. TONO DE PIEL (Único, sin contradicciones)
            // ==========================================
            // Detección genérica por defName y categoría interna (los defNames son idénticos en todos los idiomas)
            Gene chosenSkinGene = null;
            var skinGenes = basicGenes.Where(g =>
                g?.def != null &&
                (g.def.defName.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 g.def.defName.IndexOf("melanin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (g.def.displayCategory != null && (g.def.displayCategory.defName.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   g.def.displayCategory.defName.IndexOf("Cosmetic_Skin", StringComparison.OrdinalIgnoreCase) >= 0)))
            ).ToList();

            if (skinGenes.Any())
            {
                // Prioridad 1: Piel exótica (no-melanina). Si hay xenogene, tiene prioridad sobre endogene
                var exoticSkins = skinGenes.Where(g => g.def.defName.IndexOf("melanin", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                if (exoticSkins.Any())
                {
                    chosenSkinGene = exoticSkins.FirstOrDefault(g => pawn.genes?.IsXenogene(g) == true) ?? exoticSkins.First();
                }
                else
                {
                    // Prioridad 2: Gen de melanina estándar
                    chosenSkinGene = skinGenes.FirstOrDefault(g => pawn.genes?.IsXenogene(g) == true) ?? skinGenes.First();
                }
            }

            if (chosenSkinGene != null)
            {
                string skinLabel = GenePromptClassifier.GetGeneLabel(chosenSkinGene);
                if (!string.IsNullOrEmpty(skinLabel))
                {
                    traits.Add(skinLabel.ToLower());
                }
            }
            else if (pawn.story != null)
            {
                // Prioridad 3: Derivado del story.SkinColor (baseliners sin genes de piel) usando Keyed
                var skinCol = pawn.story.SkinColor;
                float brightness = (0.299f * skinCol.r) + (0.587f * skinCol.g) + (0.114f * skinCol.b);
                string skinDesc = brightness > 0.82f ? "EchoColony.Skin_Fair".Translate().ToString() :
                                  brightness > 0.68f ? "EchoColony.Skin_Light".Translate().ToString() :
                                  brightness > 0.50f ? "EchoColony.Skin_Tan".Translate().ToString() :
                                  brightness > 0.35f ? "EchoColony.Skin_Brown".Translate().ToString() :
                                  "EchoColony.Skin_Dark".Translate().ToString();
                traits.Add(skinDesc);
            }

            // ==========================================
            // B. PELO (Calvicie vs Estilo y Color unificados)
            // ==========================================
            // 1. Detección genérica de calvicie (por defName invariant o flags del motor de RimWorld)
            bool isBald = false;

            // Por gen: Hair_BaldOnly o genes mod que incluyan "Bald" en defName
            if (basicGenes.Any(g => g?.def?.defName != null &&
                g.def.defName.IndexOf("Bald", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                isBald = true;
            }

            // Por HairDef del colono: flag nativo noGraphic (calvo sin textura) o defName Bald/Shaved o styleTag Bald
            if (!isBald && pawn.story?.hairDef != null)
            {
                if (pawn.story.hairDef.noGraphic ||
                    pawn.story.hairDef.defName.Equals("Bald", StringComparison.OrdinalIgnoreCase) ||
                    pawn.story.hairDef.defName.Equals("Shaved", StringComparison.OrdinalIgnoreCase) ||
                    (pawn.story.hairDef.styleTags != null && pawn.story.hairDef.styleTags.Contains("Bald") && !pawn.story.hairDef.styleTags.Contains("HairLong")))
                {
                    isBald = true;
                }
            }

            if (isBald)
            {
                traits.Add("EchoColony.Appearance_Bald".Translate().ToString());
                // Si es calvo, NO se añade ningún color de pelo
            }
            else
            {
                // Obtenemos estilo de pelo: si tiene gen de estilo de pelo o de story.hairDef
                string hairStyle = null;
                var styleGene = basicGenes.FirstOrDefault(g => g?.def != null &&
                    (g.def.displayCategory?.defName == "HairStyle" ||
                     (g.def.defName.StartsWith("Hair_", StringComparison.OrdinalIgnoreCase) &&
                      !g.def.defName.StartsWith("HairColor_", StringComparison.OrdinalIgnoreCase) &&
                      g.def.defName.IndexOf("Bald", StringComparison.OrdinalIgnoreCase) < 0)));

                if (styleGene != null)
                {
                    hairStyle = GenePromptClassifier.GetGeneLabel(styleGene);
                }
                else if (pawn.story?.hairDef != null && !string.IsNullOrEmpty(pawn.story.hairDef.label))
                {
                    hairStyle = pawn.story.hairDef.label;
                }

                // Obtenemos color de pelo: si tiene gen de color o derivado del story.HairColor RGB
                string hairColor = null;
                var colorGene = basicGenes.FirstOrDefault(g => g?.def != null &&
                    (g.def.displayCategory?.defName == "HairColor" ||
                     g.def.displayCategory?.defName == "Cosmetic_Color" ||
                     g.def.defName.StartsWith("HairColor_", StringComparison.OrdinalIgnoreCase)));

                if (colorGene != null)
                {
                    hairColor = GenePromptClassifier.GetGeneLabel(colorGene);
                }
                else if (pawn.story != null)
                {
                    string colorName = GetApproximateColorName(pawn.story.HairColor);
                    hairColor = "EchoColony.Appearance_HairColorOnly".Translate(colorName).ToString();
                }

                // Formateo genérico mediante plantilla Keyed de idioma
                string combinedHair = null;
                if (!string.IsNullOrEmpty(hairStyle) && !string.IsNullOrEmpty(hairColor))
                {
                    combinedHair = "EchoColony.Appearance_HairWithStyle".Translate(hairColor, hairStyle).ToString();
                }
                else if (!string.IsNullOrEmpty(hairColor))
                {
                    combinedHair = hairColor;
                }
                else if (!string.IsNullOrEmpty(hairStyle))
                {
                    combinedHair = "EchoColony.Appearance_HairStyleOnly".Translate(hairStyle).ToString();
                }

                if (!string.IsNullOrEmpty(combinedHair))
                {
                    traits.Add(combinedHair.ToLower());
                }
            }

            // ==========================================
            // C. BARBA (Omitir Beard_NoBeard por Def / defName)
            // ==========================================
            // Comprobación 100% genérica por DefOf y defName interno (sin comprobar palabras de texto traducido)
            if (pawn.style?.beardDef != null &&
                pawn.style.beardDef != BeardDefOf.NoBeard &&
                !pawn.style.beardDef.defName.Equals("NoBeard", StringComparison.OrdinalIgnoreCase))
            {
                string beardLabel = pawn.style.beardDef.label;
                if (!string.IsNullOrEmpty(beardLabel))
                {
                    traits.Add(beardLabel.ToLower());
                }
            }
            else
            {
                var beardGene = basicGenes.FirstOrDefault(g => g?.def != null &&
                    g.def.defName.IndexOf("Beard", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    g.def.defName.IndexOf("NoBeard", StringComparison.OrdinalIgnoreCase) < 0);

                if (beardGene != null)
                {
                    string bLabel = GenePromptClassifier.GetGeneLabel(beardGene);
                    if (!string.IsNullOrEmpty(bLabel))
                    {
                        traits.Add(bLabel.ToLower());
                    }
                }
            }

            // ==========================================
            // D. OTROS COSMÉTICOS BÁSICOS (Ojos, etc.)
            // ==========================================
            foreach (var gene in basicGenes)
            {
                if (gene?.def == null) continue;
                string defName = gene.def.defName;

                // Omitir piel, pelo, barba y belleza (gestionados por defName invariable arriba)
                if (defName.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    defName.IndexOf("Melanin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    defName.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    defName.IndexOf("Beard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    defName.IndexOf("Beauty", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string label = GenePromptClassifier.GetGeneLabel(gene);
                if (!string.IsNullOrEmpty(label) && !traits.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    traits.Add(label.ToLower());
                }
            }

            // ==========================================
            // E. MUTACIONES FÍSICAS (Alas, cuernos, cola, etc.)
            // ==========================================
            bool hasBodyShapeGene = false;
            foreach (var gene in mutGenes)
            {
                if (gene?.def == null) continue;
                string defName = gene.def.defName;

                if (defName.StartsWith("Body_", StringComparison.OrdinalIgnoreCase) ||
                    defName.StartsWith("BodyScale", StringComparison.OrdinalIgnoreCase) ||
                    defName.StartsWith("BodyFrame", StringComparison.OrdinalIgnoreCase))
                {
                    hasBodyShapeGene = true;
                }

                string label = GenePromptClassifier.GetGeneLabel(gene);
                if (!string.IsNullOrEmpty(label) && !traits.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    traits.Add(label.ToLower());
                }
            }

            // ==========================================
            // F. COMPLEXIÓN CORPORAL (Fallback de story.bodyType mediante Keyed)
            // ==========================================
            if (!hasBodyShapeGene && pawn.story?.bodyType != null)
            {
                if (pawn.story.bodyType == BodyTypeDefOf.Thin)
                    traits.Add("EchoColony.Build_Slender".Translate().ToString());
                else if (pawn.story.bodyType == BodyTypeDefOf.Hulk)
                    traits.Add("EchoColony.Build_Muscular".Translate().ToString());
                else if (pawn.story.bodyType == BodyTypeDefOf.Fat)
                    traits.Add("EchoColony.Build_Heavyset".Translate().ToString());
            }

            // ==========================================
            // G. CONDICIONES FÍSICAS Y HEDIFFS VISIBLES (Embarazo, prótesis y amputaciones externas)
            // ==========================================
            var visibleConditions = GetVisibleBodyConditions(pawn);
            foreach (var cond in visibleConditions)
            {
                if (!string.IsNullOrEmpty(cond) && !traits.Contains(cond, StringComparer.OrdinalIgnoreCase))
                {
                    traits.Add(cond);
                }
            }

            return traits.Any() ? string.Join(", ", traits) : string.Empty;
        }

        /// <summary>
        /// Obtiene rasgos físicos y hediffs visibles externamente (embarazo avanzado, prótesis y amputaciones externas).
        /// Descarta implantes internos discretos (órganos, cerebro, columna) usando BodyPartDepth.Outside.
        /// </summary>
        public static List<string> GetVisibleBodyConditions(Pawn pawn)
        {
            var visible = new List<string>();
            if (pawn?.health?.hediffSet == null) return visible;

            // 1. Embarazo notorio (2.º y 3.er trimestre)
            var pregnancy = pawn.health.hediffSet.hediffs
                .FirstOrDefault(h => h.def != null && h.def.defName.IndexOf("Pregnant", StringComparison.OrdinalIgnoreCase) >= 0);
            if (pregnancy != null && pregnancy.CurStageIndex > 0)
            {
                if (pregnancy.CurStageIndex >= 2)
                    visible.Add("EchoColony.Appearance_HeavilyPregnant".Translate().ToString());
                else
                    visible.Add("EchoColony.Appearance_VisiblyPregnant".Translate().ToString());
            }

            // 2. Prótesis y modificaciones externas visibles (brazos, piernas, ojos, manos, pies, mandíbula, etc.)
            var externalProsthetics = pawn.health.hediffSet.hediffs
                .Where(h => h.def?.addedPartProps != null &&
                            h.Part != null &&
                            h.Part.depth == BodyPartDepth.Outside &&
                            IsExternalVisiblePart(h.Part))
                .Take(3)
                .ToList();

            foreach (var p in externalProsthetics)
            {
                string pLabel = p.Label;
                string partLabel = p.Part.Label;
                if (!string.IsNullOrEmpty(pLabel) && !string.IsNullOrEmpty(partLabel))
                {
                    visible.Add($"{pLabel.ToLower()} ({partLabel.ToLower()})");
                }
                else if (!string.IsNullOrEmpty(pLabel))
                {
                    visible.Add(pLabel.ToLower());
                }
            }

            // 3. Pérdidas y amputaciones externas notorias (sin ojos, sin orejas, sin brazos, sin piernas, sin nariz)
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors()
                .Where(m => m.Part != null &&
                            m.Part.depth == BodyPartDepth.Outside &&
                            IsExternalVisiblePart(m.Part))
                .ToList();

            // Especial: Ojos faltantes
            var missingEyes = missingParts.Where(m => m.Part.def.defName.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (missingEyes.Any())
            {
                if (missingEyes.Count > 1)
                    visible.Add("EchoColony.Appearance_BlindBothMissing".Translate().ToString());
                else
                    visible.Add("EchoColony.Appearance_MissingEye".Translate().ToString());
            }

            // Otros miembros o partes visibles faltantes (brazo, pierna, nariz, oreja)
            var otherMissing = missingParts
                .Where(m => !missingEyes.Contains(m) &&
                            (m.Part.def.defName.IndexOf("Nose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             m.Part.def.defName.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             m.Part.def.defName.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             m.Part.def.defName.IndexOf("Ear", StringComparison.OrdinalIgnoreCase) >= 0))
                .Take(2);

            foreach (var m in otherMissing)
            {
                visible.Add("EchoColony.Appearance_MissingPart".Translate(m.Part.Label.ToLower()).ToString());
            }

            return visible;
        }

        private static bool IsExternalVisiblePart(BodyPartRecord part)
        {
            if (part == null) return false;
            if (part.depth != BodyPartDepth.Outside) return false;

            string dName = part.def?.defName ?? "";
            return dName.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Nose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Ear", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Jaw", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dName.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
