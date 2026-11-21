using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace EchoColony
{
    public class GeneAIPromptDef : Def
    {
        public string targetGene;           // defName del GeneDef en RimWorld
        public string customLabel;          // Nombre simplificado alternativo (opcional)
        public string customCategory;       // Categoría para clasificación ("Cosmetic", "BasicCosmetic", "Trait", "Ability", "Needs", "Ignore", etc.)
        public string promptDescription;    // Explicación concisa y optimizada para la IA
        public string behavioralDirective;  // Instrucción explícita de conducta/rol para el LLM
        public bool ignore;                 // Si es true, el gen se descarta por completo de todos los prompts
    }

    public class XenotypeAIPromptDef : Def
    {
        public string targetXenotype;       // defName del XenotypeDef en RimWorld
        public string promptDescription;    // Explicación concisa y optimizada para la IA
    }
}
