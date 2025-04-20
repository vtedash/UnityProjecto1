using UnityEngine;
using TMPro; // Necesario para TextMeshPro
using Pathfinding; // Necesario para AIPath

public class BattleManager : MonoBehaviour // <<<--- ¡ASEGÚRATE DE QUE EL NOMBRE DE LA CLASE ES CORRECTO!
{
    [Header("Setup")]
    public GameObject luchadorPrefab;
    public Transform spawnPoint1;
    public Transform spawnPoint2;
    // Eliminamos las referencias a CharacterStats fijas

    [Header("Team Colors")]
    public Color team1Color = Color.blue;
    public Color team2Color = Color.red;

    [Header("UI References")]
    public TextMeshProUGUI textVidaLeft;
    public TextMeshProUGUI textVidaRight;

    // Referencias a instancias
    private GameObject luchadorInstance1;
    private GameObject luchadorInstance2;
    private HealthSystem health1;
    private HealthSystem health2;
    private LuchadorAIController ai1; // <<<--- Ahora debería encontrar esta clase
    private LuchadorAIController ai2; // <<<--- Ahora debería encontrar esta clase

    // Referencia necesaria para configurar arma por defecto si falta
     [Header("Defaults")]
     public WeaponData defaultWeapon; // ARRASTRA AQUÍ TU ASSET 'Fist.asset' en el Inspector

    void Start()
    {
        // Validaciones iniciales
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-5, 1, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(5, 1, 0));
        if (luchadorPrefab == null) { Debug.LogError("Prefab no asignado!", this); enabled = false; return; }
        if (textVidaLeft == null || textVidaRight == null) { Debug.LogError("Textos de vida UI no asignados!", this); enabled = false; return; }
         if (defaultWeapon == null) { Debug.LogError("Asigna el WeaponData 'Fist' al campo Default Weapon en BattleManager!", this); enabled = false; return; }

        StartBattle(); // Llama a StartBattle sin argumentos por ahora
    }

    Transform CreateSpawnPoint(string name, Vector3 position) {
        GameObject sp = new GameObject(name); sp.transform.position = position;
        sp.transform.SetParent(this.transform);
        // Debug.LogWarning($"Spawn point '{name}' creado en {position}. Ajústalo si es necesario."); // Comentado para reducir spam
        return sp.transform;
    }

    void StartBattle() {
        Debug.Log("Iniciando batalla...");

        // Instanciar Luchador 1 (Player)
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = "Luchador_Alpha (Player)";
        luchadorInstance1.tag = "Player";
        ConfigureLuchador(luchadorInstance1, "Enemy", team1Color); // Enemigo es tag "Enemy"
        health1 = luchadorInstance1.GetComponent<HealthSystem>();
        ai1 = luchadorInstance1.GetComponent<LuchadorAIController>(); // Obtiene la IA

        // Instanciar Luchador 2 (Enemy)
        luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
        luchadorInstance2.name = "Luchador_Beta (Enemy)";
        luchadorInstance2.tag = "Enemy";
        ConfigureLuchador(luchadorInstance2, "Player", team2Color); // Enemigo es tag "Player"
        health2 = luchadorInstance2.GetComponent<HealthSystem>();
        ai2 = luchadorInstance2.GetComponent<LuchadorAIController>(); // Obtiene la IA

        // Validar componentes post-instanciación
        if (health1 == null || health2 == null || ai1 == null || ai2 == null) {
             Debug.LogError("Error al obtener HealthSystem o LuchadorAIController después de instanciar.", this);
             enabled = false; return;
        }

         // Inicializar recursos DESPUÉS de configurar todo
         luchadorInstance1.GetComponent<CharacterData>()?.InitializeResourcesAndCooldowns();
         luchadorInstance2.GetComponent<CharacterData>()?.InitializeResourcesAndCooldowns();


        // Suscribir UI
        if (health1 != null) {
            health1.OnHealthChanged.AddListener(UpdateUIHealthLeft);
            UpdateUIHealthLeft(health1.CurrentHealth, health1.MaxHealth);
        } else { textVidaLeft.text = "Vida: ERR"; }

        if (health2 != null) {
            health2.OnHealthChanged.AddListener(UpdateUIHealthRight);
            UpdateUIHealthRight(health2.CurrentHealth, health2.MaxHealth);
        } else { textVidaRight.text = "Vida: ERR"; }


        // Iniciar IAs (si existen)
        ai1?.InitializeAI(); // Usa el operador '?' por seguridad
        ai2?.InitializeAI();
        Debug.Log("Batalla lista.");
    }

    // ConfigureLuchador ya NO usa CharacterStats
    void ConfigureLuchador(GameObject instance, string enemyTagToSet, Color teamColorToSet) {
        if (instance == null) { Debug.LogError("Instancia nula en ConfigureLuchador."); return; }

        CharacterData data = instance.GetComponent<CharacterData>();
        if (data == null) { Debug.LogError($"CharacterData no encontrado en {instance.name}", instance); return; }

         // Asegura arma por defecto si falta en el prefab
         if (data.equippedWeapon == null) {
             data.equippedWeapon = defaultWeapon;
              Debug.LogWarning($"Arma no asignada en prefab {instance.name}. Asignando por defecto: {defaultWeapon.name}");
         }

        LuchadorAIController ai = instance.GetComponent<LuchadorAIController>();
        if (ai != null) { ai.enemyTag = enemyTagToSet; }
        // else { Debug.LogWarning($"LuchadorAIController no encontrado en {instance.name}", instance); } // Puede ser normal si es controlado por jugador

        // AIPath se configura en InitializeAI ahora
        AIPath aiPath = instance.GetComponent<AIPath>();
        if(aiPath == null) { Debug.LogWarning($"AIPath no encontrado en {instance.name}", instance); }

        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null) { sr.color = teamColorToSet; }

        Debug.Log($"Luchador '{instance.name}' configurado. Tag: {instance.tag}, EnemyTag AI: {enemyTagToSet}");
    }

    // --- UI Updates ---
    void UpdateUIHealthLeft(float currentHealth, float maxHealth) { if (textVidaLeft != null) textVidaLeft.text = $"Vida:{currentHealth:F0}"; }
    void UpdateUIHealthRight(float currentHealth, float maxHealth) { if (textVidaRight != null) textVidaRight.text = $"Vida:{currentHealth:F0}"; }

    // --- Update Fin Pelea ---
    void Update() {
        if (!this.enabled) return;
        bool alphaAlive = (health1 != null && health1.IsAlive());
        bool betaAlive = (health2 != null && health2.IsAlive());
        bool battleOver = false;

        if (!alphaAlive && betaAlive) { Debug.Log($"¡{luchadorInstance2?.name ?? "Luchador Beta"} GANA!"); TriggerCelebration(luchadorInstance2); battleOver = true; }
        else if (!betaAlive && alphaAlive) { Debug.Log($"¡{luchadorInstance1?.name ?? "Luchador Alpha"} GANA!"); TriggerCelebration(luchadorInstance1); battleOver = true; }
        else if (!alphaAlive && !betaAlive) { Debug.Log("¡EMPATE!"); battleOver = true; }

        if (battleOver) {
            Debug.Log("Fin de la batalla.");
            // Aquí irá la lógica de XP y volver al menú en el siguiente paso
            this.enabled = false; // Desactiva el manager por ahora
        }
    }

    // --- Celebración ---
    void TriggerCelebration(GameObject winner) {
        if (winner != null) {
            LuchadorAIController winnerAI = winner.GetComponent<LuchadorAIController>();
            if (winnerAI != null && winnerAI.enabled) { winnerAI.StartCelebrating(); }
            // else { Debug.LogWarning($"No se pudo iniciar celebración para {winner.name}.", winner); } // Comentado para reducir spam
        } else { Debug.LogWarning("Intento de celebración para ganador nulo."); }
    }
}