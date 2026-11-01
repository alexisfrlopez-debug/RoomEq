using System.Threading;

namespace RoomEq.App;

/// <summary>
/// Garde d'instance unique (D26, v3.9.0) : l'installateur Velopack lance
/// l'application dès la fin de l'installation — si le Setup est exécuté deux
/// fois (double-clic, relance, retour du navigateur) ou si un raccourci est
/// ouvert pendant un démarrage lent, PLUSIEURS instances cohabitaient :
/// fenêtres en double, mixer.json corrompu par des écritures concurrentes et
/// conflits d'accès aux périphériques WASAPI. Un mutex nommé par session
/// utilisateur (« Local\ » — l'installation est elle-même par utilisateur)
/// ferme immédiatement toute instance surnuméraire, silencieusement.
/// Le mode élevé « --apo-setup » passe AVANT le garde : le processus
/// d'élévation doit toujours pouvoir tourner à côté de l'application.
/// </summary>
public static class SingleInstanceGuard
{
    /// <summary>
    /// Tente de devenir l'instance unique. Retourne un jeton à conserver
    /// vivant (using) tant que l'application tourne, ou null si une instance
    /// existe déjà — l'appelant doit alors se terminer immédiatement.
    /// </summary>
    /// <param name="name">Nom du mutex (suffixe unique par application).</param>
    public static IDisposable? Acquire(string name)
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, name: @"Local\" + name, out bool createdNew);
            if (createdNew)
                return new Lease(mutex);
            mutex.Dispose(); // détenu par l'instance existante : se taire et partir
            return null;
        }
        catch
        {
            // mutex indisponible (sécurité restrictive) : ne jamais empêcher
            // l'application de démarrer — on accepte le risque de doublon
            return new NullLease();
        }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); } catch { /* pas détenu : rien */ }
            mutex.Dispose();
        }
    }

    private sealed class NullLease : IDisposable
    {
        public void Dispose() { }
    }
}
