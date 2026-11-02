namespace RoomEq.Audio.SystemApo;

/// <summary>Une étape de l'auto-test de la DLL APO (v3.9.8).</summary>
/// <param name="Name">Nom court de l'étape (« Chargement », « CoCreateInstance »…).</param>
/// <param name="Passed">L'étape a-t-elle réussi ?</param>
/// <param name="Detail">Détail lisible : HRESULT décodé, code Win32, mesures.</param>
public sealed record ApoSelfTestStep(string Name, bool Passed, string Detail)
{
    public static ApoSelfTestStep Ok(string name, string detail) => new(name, true, detail);
    public static ApoSelfTestStep Fail(string name, string detail) => new(name, false, detail);
}

/// <summary>
/// Rapport de l'auto-test COM complet de la DLL APO (v3.9.8) : chargement,
/// instanciation, interfaces, propriétés d'enregistrement, verrouillage et
/// traitement d'un signal témoin — tout ce que fait le moteur audio de
/// Windows (audiodg), exécuté DANS le processus d'installation élevé AVANT
/// de toucher au registre. Un échec ici bloque l'activation sans jamais
/// mettre la sortie en danger, et nomme l'étape exacte. Enregistrement de
/// données PUR : formatage testable sous Linux, l'exécution vit dans
/// <see cref="ApoDllSelfTest"/>.
/// </summary>
public sealed record ApoSelfTestReport(bool Passed, IReadOnlyList<ApoSelfTestStep> Steps)
{
    /// <summary>Première étape en échec (null si tout est passé).</summary>
    public ApoSelfTestStep? FirstFailure => Steps.FirstOrDefault(s => !s.Passed);

    /// <summary>
    /// Résumé français compact : « OK — 9 étapes (chargement, instanciation,
    /// verrouillage, traitement : la DLL est saine) » ou « ÉCHEC à l'étape
    /// « CoCreateInstance » : 0x80070005 — accès refusé ».
    /// </summary>
    public string Summary()
    {
        var failure = FirstFailure;
        return failure == null
            ? "OK (" + Steps.Count + " étapes : chargement, instanciation COM, interfaces, "
              + "enregistrement, verrouillage, traitement — la DLL elle-même est saine)"
            : "ÉCHEC à l'étape « " + failure.Name + " » : " + failure.Detail;
    }
}
