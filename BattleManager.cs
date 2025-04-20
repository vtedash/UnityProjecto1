// File: BattleManager.cs
using UnityEngine;
using TMPro; // Necesario para TextMeshPro

public class BattleManager : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Prefab del personaje luchador.")]
    public GameObject luchadorPrefab;
    [Tooltip("Punto de aparición para el luchador 1.")]
    public Transform spawnPoint1;
    [Tooltip("Punto de aparición para el luchador 2.")]
    public Transform spawnPoint2;
    [Tooltip("Stats para el luchador 1.")]
    public CharacterStats statsLuchador1;
    [Tooltip("Stats para el luchador 2.")]
    public CharacterStats statsLuchador2;

    [Header("Team Colors")]
    [Tooltip("Color para el equipo 1 (normalmente 'Player').")]
    public Color team1Color = Color.blue;
    [Tooltip("Color para el equipo 2 (normalmente 'Enemy').")]
    public Color team2Color = Color.red;

    [Header("UI References")]
    [Tooltip("El objeto TextMeshProUGUI que muestra la vida del luchador izquierdo (Player).")]
    public TextMeshProUGUI textVidaLeft;
    [Tooltip("El objeto TextMeshProUGUI que muestra la vida del luchador derecho (Enemy).")]
    public TextMeshProUGUI textVidaRight;

    // --- Runtime References ---
    private GameObject luchadorInstance1;
    private GameObject luchadorInstance2;
    private HealthSystem health1;
    private HealthSystem health2;
    private LuchadorAIController ai1;
    private LuchadorAIController ai2;

    void Start()
    {
        // --- Validaciones Iniciales ---
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-5, 1, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(5, 1, 0));
        if (statsLuchador1 == null || statsLuchador2 == null) { Debug.LogError("Stats no asignados!", this); enabled = false; return; }
        if (luchadorPrefab == null) { Debug.LogError("Prefab no asignado!", this); enabled = false; return; }
        if (textVidaLeft == null || textVidaRight == null) { Debug.LogError("Textos de vida UI no asignados!", this); enabled = false; return; }

        StartBattle();
    }

    Transform CreateSpawnPoint(string name, Vector3 position) {
        GameObject sp = new GameObject(name); sp.transform.position = position;
        sp.transform.SetParent(this.transform);
        Debug.LogWarning($"Spawn point '{name}' no asignado. Se creó uno en {position}. Ajústalo si es necesario.");
        return sp.transform;
    }

    void StartBattle() {
        Debug.Log("Iniciando batalla...");

        // --- Instanciar y Configurar Luchador 1 (Player - Izquierda) ---
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = "Luchador_Alpha"; luchadorInstance1.tag = "Player";
        ConfigureLuchador(luchadorInstance1, statsLuchador1, "Enemy", team1Color);
        health1 = luchadorInstance1.GetComponent<HealthSystem>();
        ai1 = luchadorInstance1.GetComponent<LuchadorAIController>();

        // --- Instanciar y Configurar Luchador 2 (Enemy - Derecha) ---
        luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
        luchadorInstance2.name = "Luchador_Beta"; luchadorInstance2.tag = "Enemy";
        ConfigureLuchador(luchadorInstance2, statsLuchador2, "Player", team2Color);
        health2 = luchadorInstance2.GetComponent<HealthSystem>();
        ai2 = luchadorInstance2.GetComponent<LuchadorAIController>();

        // --- Validar Cacheo ---
        if (health1 == null || health2 == null || ai1 == null || ai2 == null) {
             Debug.LogError("Error al obtener componentes después de instanciar.", this);
             enabled = false; return;
        }

        // --- Suscribir UI a eventos de vida ---
        if (health1 != null) {
            health1.OnHealthChanged.AddListener(UpdateUIHealthLeft);
            // *** CORREGIDO: Usa propiedades públicas de HealthSystem ***
            UpdateUIHealthLeft(health1.CurrentHealth, health1.MaxHealth);
        } else { textVidaLeft.text = "Vida: ERR"; } // Error si health1 es null

        if (health2 != null) {
            health2.OnHealthChanged.AddListener(UpdateUIHealthRight);
             // *** CORREGIDO: Usa propiedades públicas de HealthSystem ***
            UpdateUIHealthRight(health2.CurrentHealth, health2.MaxHealth);
        } else { textVidaRight.text = "Vida: ERR"; } // Error si health2 es null
        // --- Fin Suscripción UI ---

        // --- Iniciar IAs ---
        Debug.Log("Inicializando IA de Luchador Alpha..."); ai1.InitializeAI();
        Debug.Log("Inicializando IA de Luchador Beta..."); ai2.InitializeAI();
        Debug.Log("Batalla lista.");
    }

    // ConfigureLuchador (Sin cambios respecto a la versión anterior)
    void ConfigureLuchador(GameObject instance, CharacterStats stats, string enemyTagToSet, Color teamColorToSet) {
        if (instance == null) { Debug.LogError("Instancia nula en ConfigureLuchador."); return; }
        CharacterData data = instance.GetComponent<CharacterData>(); if (data != null) { data.baseStats = stats; } else Debug.LogError($"CharacterData no encontrado en {instance.name}", instance);
        LuchadorAIController ai = instance.GetComponent<LuchadorAIController>(); if (ai != null) { ai.enemyTag = enemyTagToSet; } else Debug.LogError($"LuchadorAIController no encontrado en {instance.name}", instance);
        Pathfinding.AIPath aiPath = instance.GetComponent<Pathfinding.AIPath>(); if(aiPath != null && data != null && data.baseStats != null) { aiPath.maxSpeed = data.baseStats.movementSpeed; } else if (aiPath == null) { Debug.LogError($"AIPath no encontrado en {instance.name}", instance); } else { Debug.LogWarning($"No se pudo configurar AIPath.maxSpeed para {instance.name}.", instance); }
        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>(); if (sr != null) { sr.color = teamColorToSet; } else Debug.LogWarning($"SpriteRenderer no encontrado en {instance.name}", instance);
        Debug.Log($"Luchador '{instance.name}' configurado. Tag: {instance.tag}, EnemyTag AI: {enemyTagToSet}");
    }

    // UpdateUIHealthLeft (Sin cambios)
    void UpdateUIHealthLeft(float currentHealth, float maxHealth) {
        if (textVidaLeft != null) { textVidaLeft.text = $"Vida:{currentHealth:F0}"; }
    }
    // UpdateUIHealthRight (Sin cambios)
    void UpdateUIHealthRight(float currentHealth, float maxHealth) {
        if (textVidaRight != null) { textVidaRight.text = $"Vida:{currentHealth:F0}"; }
    }

    // Update (Sin cambios)
    void Update() {
        if (!this.enabled) return; bool alphaAlive = (health1 != null && health1.IsAlive()); bool betaAlive = (health2 != null && health2.IsAlive()); bool battleOver = false;
        if (!alphaAlive && betaAlive) { Debug.Log($"¡{luchadorInstance2?.name ?? "Luchador Beta"} GANA!"); TriggerCelebration(luchadorInstance2); battleOver = true; }
        else if (!betaAlive && alphaAlive) { Debug.Log($"¡{luchadorInstance1?.name ?? "Luchador Alpha"} GANA!"); TriggerCelebration(luchadorInstance1); battleOver = true; }
        else if (!alphaAlive && !betaAlive) { Debug.Log("¡EMPATE!"); battleOver = true; }
        if (battleOver) { Debug.Log("Fin de la batalla. BattleManager desactivado."); this.enabled = false; }
    }

    // TriggerCelebration (Sin cambios)
    void TriggerCelebration(GameObject winner) { if (winner != null) { LuchadorAIController winnerAI = winner.GetComponent<LuchadorAIController>(); if (winnerAI != null && winnerAI.enabled) { winnerAI.StartCelebrating(); } else { Debug.LogWarning($"No se pudo iniciar celebración para {winner.name}.", winner); } } else { Debug.LogWarning("Intento de celebración para ganador nulo."); } }
}