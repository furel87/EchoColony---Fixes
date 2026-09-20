using Verse;

namespace EchoColony.Util
{
    /// <summary>
    /// Definición XML para parchear cómo la IA interpreta prótesis, implantes y afecciones.
    /// </summary>
    public class HediffAIPromptDef : Def
    {
        // defName del Hediff al que aplica (ej: "Joywire", "PegLeg", "Alzheimers")
        public string targetHediff;

        // Nombre limpio alternativo (opcional, ej: "peg leg")
        public string customLabel;

        // Nota explicativa funcional inline (opcional, ej: "brain chip forcing continuous dopamine release")
        public string functionalNote;

        // Instrucción de rol/conducta (opcional, ej: "You are unnaturally happy at all times.")
        public string behavioralDirective;
    }
}

