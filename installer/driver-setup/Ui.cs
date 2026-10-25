// ============================================================================
//  Ui — couche d'affichage de RoomEqDriverSetup (v2.2.0).
//
//  Par defaut : console (comportement historique des options --status,
//  --install, --uninstall, --mode-normal, inchangement). L'interface
//  graphique (GuiRunner) rebranche ces delegates vers la fenetre AVANT de
//  lancer une action : le journal coule dans la zone de texte, et les
//  questions s'affichent en boites de dialogue Oui/Non.
//
//  Les textes produits par la logique restent en ASCII volontairement
//  (robustesse consoles heritage) ; l'habillage graphique (boutons,
//  titres, libelles de la fenetre) utilise l'accentue.
// ============================================================================
using System;

namespace RoomEqDriverSetup
{
    internal static class Ui
    {
        /// <summary>Ligne de journal ordinaire (etape, resultat, verdict...).</summary>
        public static Action<string> Say = Console.WriteLine;

        /// <summary>Titre de section (RESULTAT, VERDICT...) — mis en valeur en GUI.</summary>
        public static Action<string> Heading = null;   // null -> Say

        /// <summary>Ligne d'erreur ([ERREUR]...) — en rouge en GUI.</summary>
        public static Action<string> SayError = null;  // null -> Say

        /// <summary>
        /// Question fermee. defautOui = reponse si entree vide (console) ou
        /// bouton propose par defaut (GUI). Retourne true = Oui.
        /// </summary>
        public static Func<string, bool, bool> Confirm = ConfirmConsole;

        /// <summary>« Appuyez sur Entree » : attente en console, no-op en GUI.</summary>
        public static Action Pause = PauseConsole;

        /// <summary>En-tete de section tolerant (utilisable avant rebranchement GUI).</summary>
        public static void HeadingSafe(string title)
        {
            if (Heading != null) Heading(title);
            else Console.WriteLine(title);
        }

        /// <summary>Ligne d'erreur tolerante (utilisable avant rebranchement GUI).</summary>
        public static void ErrorSafe(string message)
        {
            if (SayError != null) SayError(message);
            else Console.WriteLine(message);
        }

        // ------------------------------------------------ console par defaut

        private static bool ConfirmConsole(string question, bool defautOui)
        {
            Console.Write(question + " (" + (defautOui ? "O/n" : "o/N") + ") : ");
            string rep;
            try { rep = Console.ReadLine(); } catch { rep = ""; }
            if (defautOui)
                return rep == null || rep.Trim().ToLowerInvariant().StartsWith("o");
            return rep != null && rep.Trim().ToLowerInvariant().StartsWith("o");
        }

        private static void PauseConsole()
        {
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
        }
    }
}
