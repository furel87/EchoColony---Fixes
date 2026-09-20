using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace EchoColony
{
    [StaticConstructorOnStartup]
    public static class GenePromptClassifier
    {
        private static readonly Dictionary<string, string> CustomDescriptions = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> LabelOverrides = new Dictionary<string, string>();

        static GenePromptClassifier()
        {
            LoadXMLDefinitions();
        }

        private static void LoadXMLDefinitions()
        {
            foreach (var promptDef in DefDatabase<GeneAIPromptDef>.AllDefs)
            {
                if (string.IsNullOrEmpty(promptDef.targetGene)) continue;

                if (!string.IsNullOrEmpty(promptDef.customLabel))
                    LabelOverrides[promptDef.targetGene] = promptDef.customLabel;

                if (!string.IsNullOrEmpty(promptDef.promptDescription))
                    CustomDescriptions[promptDef.targetGene] = promptDef.promptDescription;
            }
        }

        /// <summary>
        /// Formatea el gen para el prompt. Prioriza el XML personalizado; si no existe, usa la descripción nativa recortada.
        /// </summary>
        public static string FormatGeneForPrompt(Gene gene)
        {
            if (gene?.def == null) return string.Empty;

            string defName = gene.def.defName;

            // 1. Etiqueta (Usa override si existe, si no la del juego)
            string label = LabelOverrides.TryGetValue(defName, out var customLabel)
                ? customLabel
                : gene.def.label;

            if (string.IsNullOrEmpty(label)) return string.Empty;

            // 2. Si existe una descripción optimizada por XML, LA USAMOS DIRECTAMENTE
            if (CustomDescriptions.TryGetValue(defName, out var customDesc))
            {
                return $"{label} ({customDesc})";
            }

            // 3. FALLBACK: Si no hay XML personalizado, procesamos la descripción original del juego
            string desc = gene.def.description;
            if (!string.IsNullOrEmpty(desc))
            {
                // Limpieza de tags XML nativos de RimWorld (<color>, <i>, etc.)
                desc = System.Text.RegularExpressions.Regex.Replace(desc, "<.*?>", "").Trim();

                // Extraemos la primera oración para mantenerlo corto
                int periodIndex = desc.IndexOf('.');
                if (periodIndex > 0)
                    desc = desc.Substring(0, periodIndex);

                if (desc.Length > 80)
                    desc = desc.Substring(0, 77) + "...";

                return $"{label} ({desc})";
            }

            return label;
        }
    }
}
