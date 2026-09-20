using EchoColony.Util;
using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace EchoColony
{
    public static class HealthPromptClassifier
    {
        public static List<Hediff> GetHediffPawn (Pawn pawn)
        {
            if (pawn.health?.hediffSet?.hediffs == null) return new List<Hediff>();

            var hediffs = pawn.health.hediffSet.hediffs.Where(h => h.Visible).ToList();
            return hediffs;
        }
        public static void AppendCriticalAndCapacities(Pawn pawn, List<Hediff> hediffs, List<string> healthStatus)
        {
            var details = new List<string>();

            // 1. Detección de Embarazo (Biotech, RJW y otros mods)
            var pregnancy = hediffs.FirstOrDefault(h =>
                h.def.defName.ToLower().Contains("pregnant") ||
                h.def.defName.ToLower().Contains("pregnancy"));

            if (pregnancy != null)
            {
                // Opcional: Si el hediff tiene severidad o etapas (ej: avanzado), 
                // usará la etiqueta oficial traducida del juego/mod (ej: "pregnant" o "pregnant (advanced)")
                string pregnancyLabel = pregnancy.LabelCap.ToLower();
                details.Add(string.IsNullOrEmpty(pregnancyLabel) ? "pregnant" : pregnancyLabel);
            }

            float healthPercent = pawn.health.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (healthPercent < 0.25f) details.Add("barely clinging to life");
            else if (healthPercent < 0.5f) details.Add("badly injured and weakened");
            else if (healthPercent < 0.75f) details.Add("wounded but functional");

            float totalBleedRate = pawn.health.hediffSet.BleedRateTotal;
            if (totalBleedRate > 0.4f) details.Add("bleeding profusely");
            else if (totalBleedRate > 0.1f) details.Add("bleeding from wounds");

            float pain = pawn.health.hediffSet.PainTotal;
            if (pain > 0.4f) details.Add("in severe pain");
            else if (pain > 0.2f) details.Add("dealing with pain");

            if (pawn.health?.capacities != null)
            {
                var consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                var moving = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                var manipulation = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);

                if (consciousness < 0.6f) details.Add("impaired consciousness");
                if (moving < 0.5f) details.Add("can barely walk");
                if (manipulation < 0.5f) details.Add("can barely use hands");
            }

            if (details.Any())
            {
                healthStatus.Add($"Physical state: {string.Join(", ", details)}");
            }
        }

        public static void AppendInjuries(List<Hediff> hediffs, List<string> healthStatus)
        {
            var injuries = hediffs.OfType<Hediff_Injury>()
                .Where(i => i.Visible)
                .ToList();

            if (!injuries.Any()) return;

            // 1. Ordenar todas las heridas de mayor a menor severidad
            var sortedInjuries = injuries.OrderByDescending(i => i.Severity).ToList();

            // 2. Extraer las 3 más graves para detalle explícito
            var top3 = sortedInjuries.Take(3).ToList();

            var topDetails = top3
                .GroupBy(i => i.Part != null ? i.Part.LabelCap.ToString() : "Body")
                .Select(group =>
                {
                    int count = group.Count();
                    string part = group.Key;

                    if (count > 1)
                    {
                        return $"{count} wounds on {part}";
                    }

                    var injury = group.First();
                    string sevLabel = injury.Severity > 10f ? "severe" : injury.Severity > 5f ? "serious" : "minor";
                    return $"{sevLabel} {injury.def.label} on {part}";
                });

            string topInjuriesText = string.Join(", ", topDetails);

            // 3. Evaluar la gravedad de las heridas restantes (de la 4ª en adelante)
            var remaining = sortedInjuries.Skip(3).ToList();

            if (remaining.Any())
            {
                int severeCount = remaining.Count(i => i.Severity > 10f);
                int seriousCount = remaining.Count(i => i.Severity > 5f && i.Severity <= 10f);
                int minorCount = remaining.Count(i => i.Severity <= 5f);

                var summaryParts = new List<string>();
                if (severeCount > 0) summaryParts.Add($"{severeCount} other severe");
                if (seriousCount > 0) summaryParts.Add($"{seriousCount} serious");
                if (minorCount > 0) summaryParts.Add($"{minorCount} minor");

                string remainingSummary = string.Join(", ", summaryParts);

                healthStatus.Add($"Wounds: {topInjuriesText} (plus {remainingSummary} additional wounds across your body).");
            }
            else
            {
                healthStatus.Add($"Wounds: {topInjuriesText}.");
            }
        }

        public static void AppendProstheticsAndMissingParts(Pawn pawn, List<Hediff> hediffs, List<string> healthStatus)
        {
            // 1. Obtener todas las partes sustituidas por una prótesis
            var prosthetics = hediffs.Where(h => h.def.addedPartProps != null && h.Part != null).ToList();
            var prostheticParts = new HashSet<BodyPartRecord>(prosthetics.Select(h => h.Part));

            // 2. Obtener partes faltantes reales (que no tengan una prótesis encima)
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors()
                    .Where(h => h.Part != null && !IsPartCoveredByProsthetic(h.Part, prostheticParts))
                    .Select(h => h.Part.Label)
                    .Distinct()
                    .ToList();

            var details = new List<string>();

            // 3. Procesar las prótesis agrupando por su nombre limpio (ej: "bionic arm")
            if (prosthetics.Any())
            {
                var groupedByLabel = prosthetics.GroupBy(p => ProstheticClassifier.GetProstheticLabel(p));

                foreach (var group in groupedByLabel)
                {
                    string prostheticName = group.Key;
                    var parts = group.Select(p => p.Part.Label).Distinct().ToList();

                    // Si la prótesis ya incluye el nombre de la parte (ej: label "bionic arm" y part "left arm")
                    // mostramos: "bionic arm (left arm)" o si son varias: "bionic arm (left arm and right arm)"
                    details.Add($"{prostheticName} ({string.Join(" and ", parts)})");
                }
            }

            // 4. Añadir partes faltantes
            if (missingParts.Any())
            {
                details.Add($"missing ({string.Join(" and ", missingParts)})");
            }

            if (details.Any())
            {
                healthStatus.Add($"Body modifications: {string.Join(", ", details)}");
            }
        }

        public static void AppendImplants(List<Hediff> hediffs, List<string> healthStatus)
        {
            var implantHediffs = hediffs.Where(h =>
                h.def.addedPartProps == null &&
                !h.def.isBad &&
                (h is Hediff_Implant || h.def.countsAsAddedPartOrImplant)
            ).ToList();

            if (!implantHediffs.Any()) return;

            // Agrupamos por el nombre del implante (que ya incluye notas personalizadas si las hay)
            var implantDetails = implantHediffs
                .GroupBy(h => ProstheticClassifier.GetProstheticLabel(h))
                .Select(group =>
                {
                    string implantName = group.Key;

                    // Extraemos las partes del cuerpo donde están instalados
                    var parts = group
                        .Select(h => h.Part != null ? h.Part.LabelCap.ToString() : null)
                        .Where(partLabel => !string.IsNullOrEmpty(partLabel))
                        .Distinct()
                        .ToList();

                    // Si tiene localización de parte del cuerpo asociada, la añadimos entre paréntesis
                    if (parts.Any())
                    {
                        return $"{implantName} ({string.Join(" and ", parts)})";
                    }

                    // Si el implante no tiene 'Part' asignado (es un implante global), devolvemos solo el nombre
                    return implantName;
                })
                .ToList();

            if (implantDetails.Any())
            {
                healthStatus.Add($"Implants: {string.Join(", ", implantDetails)}");
            }
        }

        public static void AppendAddictionsAndWithdrawals(List<Hediff> hediffs, List<string> healthStatus)
        {
            var addictions = hediffs.Where(h => h.def?.defName != null && h.def.defName.EndsWith("Addiction")).ToList();
            var withdrawals = hediffs.Where(h => h.def?.defName != null && h.def.defName.EndsWith("Withdrawal")).ToList();

            if (!addictions.Any() && !withdrawals.Any()) return;

            string GetCleanDrugName(Hediff h)
            {
                // A) Si es un Hediff_Addiction nativo, la sustancia química nos da el nombre exacto
                if (h is Hediff_Addiction addComp && addComp.Chemical != null)
                {
                    return addComp.Chemical.label;
                }

                // B) Si no, recortamos el defName de la definición XML (ej: "AlcoholAddiction" -> "alcohol")
                string defName = h.def.defName;
                if (defName.EndsWith("Addiction"))
                    return defName.Substring(0, defName.Length - "Addiction".Length).ToLower();
                if (defName.EndsWith("Withdrawal"))
                    return defName.Substring(0, defName.Length - "Withdrawal".Length).ToLower();

                return h.def.label;
            }

            var details = new List<string>();
            var processedDrugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Procesar Adicciones
            foreach (var add in addictions)
            {
                string drugName = GetCleanDrugName(add);
                processedDrugs.Add(drugName);

                // Buscar si existe una abstinencia activa comparando directamente los nombres limpios
                var withdrawal = withdrawals.FirstOrDefault(w =>
                    GetCleanDrugName(w).Equals(drugName, StringComparison.OrdinalIgnoreCase));

                if (withdrawal != null)
                {
                    string stageLabel = withdrawal.CurStage?.label;
                    if (string.IsNullOrEmpty(stageLabel))
                    {
                        stageLabel = withdrawal.Severity > 0.7f ? "severe" : withdrawal.Severity > 0.3f ? "moderate" : "initial";
                    }

                    details.Add($"Addicted to {drugName} — currently suffering from {stageLabel} withdrawal (experiencing intense cravings and distress)");
                }
                else
                {
                    details.Add($"Addicted to {drugName} (chemically dependent, but currently satisfied)");
                }
            }

            // 2. Procesar Abstinencias huérfanas (sin el hediff de adicción principal)
            foreach (var wit in withdrawals)
            {
                string drugName = GetCleanDrugName(wit);

                if (!processedDrugs.Contains(drugName))
                {
                    processedDrugs.Add(drugName);

                    string stageLabel = wit.CurStage?.label;
                    if (string.IsNullOrEmpty(stageLabel))
                    {
                        stageLabel = wit.Severity > 0.7f ? "severe" : wit.Severity > 0.3f ? "moderate" : "initial";
                    }

                    details.Add($"Suffering from {drugName} withdrawal ({stageLabel})");
                }
            }

            if (details.Any())
            {
                healthStatus.Add($"Chemical dependencies: {string.Join("; ", details)}.");
            }
        }

        public static void AppendDiseasesAndInfections(List<Hediff> hediffs, List<string> healthStatus)
        {
            if (hediffs == null || !hediffs.Any())
                return;

            // --- A. ENFERMEDADES AGUDAS E INFECCIONES ---
            var activeDiseases = hediffs
                .Where(h => h.Visible && IsAcuteDisease(h))
                .ToList();

            if (activeDiseases.Any())
            {
                // 1. Infecciones de heridas
                var woundInfections = activeDiseases
            .Where(h => h.def.defName == "WoundInfection")
            .ToList();

                if (woundInfections.Any())
                {
                    // Construye cadenas como "infected wound on left arm"
                    var infectionDescriptions = woundInfections.Select(h =>
                    {
                        string partLabel = h.Part != null ? h.Part.Label : "body";
                        return $"infected wound on {partLabel}";
                    });

                    healthStatus.Add($"Illnesses: {string.Join(", ", infectionDescriptions)}.");
                }

                // 2. Enfermedades agudas sistémicas (Gripe, Peste, etc.)
                var systemicDiseases = activeDiseases
                    .Where(h => h.def.defName != "WoundInfection")
                    .Select(h => h.LabelCap.ToString())
                    .Distinct();

                foreach (var disease in systemicDiseases)
                {
                    healthStatus.Add($"Health Status: Sick / Currently suffering from {disease}.");
                }
            }

            // --- B. ENFERMEDADES CRÓNICAS ---
            var chronicDiseases = hediffs
                .Where(h => h.Visible && IsChronicDisease(h))
                .Select(h => h.LabelCap.ToString())
                .Distinct()
                .ToList();

            foreach (var chronic in chronicDiseases)
            {
                healthStatus.Add($"Health Status: Chronic Condition / Suffering from {chronic}.");
            }
        }
        /// <summary>
        /// Returns true if the given body part or any of its ancestors has a prosthetic or bionic installed,
        /// meaning missing part hediffs below it should not be listed as truly missing.
        /// </summary>
        private static bool IsPartCoveredByProsthetic(BodyPartRecord part, HashSet<BodyPartRecord> prostheticParts)
        {
            var current = part;
            while (current != null)
            {
                if (prostheticParts.Contains(current)) return true;
                current = current.parent;
            }
            return false;
        }

        public static bool IsAcuteDisease(Hediff hediff)
        {
            if (hediff == null || hediff.def == null)
                return false;

            HediffDef def = hediff.def;

            // 1. Descartar si no es algo negativo o si es una herida/parte faltante
            if (!def.isBad || def.chronic)
                return false;

            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart)
                return false;

            // 2. Filtro directo por infección o pensamiento de enfermedad
            if (def.isInfection || def.makesSickThought)
                return true;

            // 3. Filtro por componentes típicos de enfermedades (Inmunizables / Tratable por tiempo)
            bool hasImmunizable = def.HasComp(typeof(HediffComp_Immunizable));
            bool hasTendDuration = def.HasComp(typeof(HediffComp_TendDuration));

            if (hasImmunizable || (def.tendable && hasTendDuration))
                return true;

            // 4. Casos específicos de afecciones agudas o bloqueos mentales (Catatonic, HeartAttack, etc.)
            if (def.defName == "HeartAttack" || def.defName == "CatatonicBreakdown" || def.defName == "FoodPoisoning")
                return true;

            return false;
        }

        public static bool IsChronicDisease(Hediff hediff)
        {
            if (hediff == null || hediff.def == null)
                return false;

            // Afección negativa marcada explícitamente como crónica en el XML
            return hediff.def.isBad && hediff.def.chronic && !(hediff is Hediff_Injury);
        }

        /// <summary>
        /// Recopila todas las directivas de comportamiento (behavioralDirective) asociadas a los hediffs del colono.
        /// </summary>
        public static void AppendBehavioralDirectives(List<Hediff> hediffs, List<string> healthStatus)
        {
            if (hediffs == null || !hediffs.Any()) return;

            var directives = new List<string>();

            foreach (var h in hediffs)
            {
                if (!h.Visible) continue;

                string directive = ProstheticClassifier.GetBehavioralDirective(h.def.defName);
                if (!string.IsNullOrEmpty(directive))
                {
                    directives.Add($"- {directive}");
                }
            }

            if (directives.Any())
            {
                healthStatus.Add("Behavioral Directives:\n" + string.Join("\n", directives));
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class ProstheticClassifier
    {
        private static readonly Dictionary<string, string> LabelOverrides = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> FunctionalNotes = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> BehavioralDirectives = new Dictionary<string, string>();

        static ProstheticClassifier()
        {
            // Cargar automáticamente todos los XMLs de tipo HediffAIPromptDef al iniciar el juego
            LoadXMLDefinitions();
        }

        private static void LoadXMLDefinitions()
        {
            foreach (var promptDef in DefDatabase<HediffAIPromptDef>.AllDefs)
            {
                if (string.IsNullOrEmpty(promptDef.targetHediff)) continue;

                if (!string.IsNullOrEmpty(promptDef.customLabel))
                {
                    LabelOverrides[promptDef.targetHediff] = promptDef.customLabel;
                }

                if (!string.IsNullOrEmpty(promptDef.functionalNote))
                {
                    FunctionalNotes[promptDef.targetHediff] = promptDef.functionalNote;
                }

                if (!string.IsNullOrEmpty(promptDef.behavioralDirective))
                {
                    BehavioralDirectives[promptDef.targetHediff] = promptDef.behavioralDirective;
                }
            }
        }

        /// <summary>
        /// Permite registrar o corregir el nombre de una prótesis/implante desde un parche.
        /// </summary>
        public static void RegisterLabelOverride(string defName, string customLabel)
        {
            LabelOverrides[defName] = customLabel;
        }

        /// <summary>
        /// Permite añadir o modificar descripciones/pistas para cualquier implante o prótesis desde un parche.
        /// </summary>
        public static void RegisterFunctionalNote(string defName, string note)
        {
            FunctionalNotes[defName] = note;
        }

        public static void RegisterBehavioralDirective(string defName, string directive)
        {
            BehavioralDirectives[defName] = directive;
        }

        /// <summary>
        /// Obtiene el nombre de la prótesis y, si existe o se le pasa, añade su aclaración técnica.
        /// </summary>
        /// <param name="hediff">El Hediff del colono.</param>
        /// <param name="includeDescription">Si es true, buscará si hay una nota aclaratoria registrada y la adjuntará.</param>
        /// <param name="customDescription">Permite pasar una nota ad-hoc directamente en la llamada si fuera necesario.</param>
        public static string GetProstheticLabel(Hediff hediff, bool includeDescription = true, string customDescription = null)
        {
            if (hediff?.def == null) return "prosthetic";

            // 1. Obtener el nombre limpio
            string name = LabelOverrides.TryGetValue(hediff.def.defName, out var customLabel)
                ? customLabel
                : hediff.def.label.ToLower();

            // Si explícitamente pedimos solo el nombre, lo devolvemos tal cual
            if (!includeDescription)
            {
                return name;
            }

            // 2. Si le pasamos una descripción personalizada al vuelo en la función
            if (!string.IsNullOrEmpty(customDescription))
            {
                return $"{name} ({customDescription})";
            }

            // 3. Si tiene una nota registrada en nuestro diccionario
            if (FunctionalNotes.TryGetValue(hediff.def.defName, out var functionalNote))
            {
                return $"{name} ({functionalNote})";
            }

            // 4. SOLO PARA PROTESIS E IMPLANTES
            // Extraemos la primera frase del XML si no había nota manual
            bool isProsthetic = hediff.def.addedPartProps != null;

            if (!isProsthetic && !string.IsNullOrEmpty(hediff.def.description))
            {
                string shortDesc = GetFirstSentence(hediff.def.description);
                // Solo si es una frase corta y útil (menos de 65 caracteres)
                if (!string.IsNullOrEmpty(shortDesc) && shortDesc.Length < 65)
                {
                    return $"{name} [{shortDesc}]";
                }
            }

            // 5. Fallback para prótesis sin nota manual o implantes sin descripción útil
            return name;
        }

        public static string GetBehavioralDirective(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            BehavioralDirectives.TryGetValue(defName, out var directive);
            return directive;
        }

        private static string GetFirstSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            int periodIndex = text.IndexOf('.');
            if (periodIndex > 0)
            {
                return text.Substring(0, periodIndex).Trim().ToLower();
            }
            return text.Trim().ToLower();
        }

    }
}
