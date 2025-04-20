using UnityEngine;
using TMPro; // Necesario para TextMeshPro
using Pathfinding; // Necesario para AIPath

public class BattleManager : MonoBehaviour
{
    [Header("Setup")]
    public GameObject luchadorPrefab;
    public Transform spawnPoint1;
    public Transform spawnPoint2;
    // Eliminamos las referencias a CharacterStats

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
    private LuchadorAIController ai1;
    private LuchadorAIController ai2;

    // Referencia necesaria para configurar arma por defecto si falta
     [Header("Defaults")]
     public WeaponData defaultWeapon; // ARRASTRA AQUÍ TU ASSET 'Fist.asset'

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
        Debug.LogWarning($"Spawn point '{name}' creado en {position}. Ajústalo si es necesario.");
        return sp.transform;
    }

    // StartBattle ahora no recibe argumentos (temporalmente)
    void StartBattle() {
        Debug.Log("Iniciando batalla...");

        // Instanciar Luchador 1 (Player)
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = "Luchador_Alpha (Player)";
        luchadorInstance1.tag = "Player"; // Tag para IA enemiga
        ConfigureLuchador(luchadorInstance1, "Enemy", team1Color); // Pasa tag enemigo
        health1 = luchadorInstance1.GetComponent<HealthSystem>();
        ai1 = luchadorInstance1.GetComponent<LuchadorAIController>();

        // Instanciar Luchador 2 (Enemy)
        luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
        luchadorInstance2.name = "Luchador_Beta (Enemy)";
        luchadorInstance2.tag = "Enemy"; // Tag para IA del jugador (si la hubiera) o para diferenciar
        ConfigureLuchador(luchadorInstance2, "Player", team2Color); // Pasa tag enemigo (Player)
        health2 = luchadorInstance2.GetComponent<HealthSystem>();
        ai2 = luchadorInstance2.GetComponent<LuchadorAIController>();

        // Validar componentes post-instanciación
        if (health1 == null || health2 == null || ai1 == null || ai2 == null) {
             Debug.LogError("Error al obtener componentes después de instanciar.", this);
             enabled = false; return;
        }

         // Inicializar recursos DESPUÉS de configurar todo
         luchadorInstance1.GetComponent<CharacterData>()?.InitializeResourcesAndCooldowns();
         luchadorInstance2.GetComponent<CharacterData>()?.InitializeResourcesAndCooldowns();


        // Suscribir UI (Usa las propiedades de HealthSystem)
        if (health1 != null) {
            health1.OnHealthChanged.AddListener(UpdateUIHealthLeft);
            UpdateUIHealthLeft(health1.CurrentHealth, health1.MaxHealth); // Actualiza UI inicial
        } else { textVidaLeft.text = "Vida: ERR"; }

        if (health2 != null) {
            health2.OnHealthChanged.AddListener(UpdateUIHealthRight);
            UpdateUIHealthRight(health2.CurrentHealth, health2.MaxHealth); // Actualiza UI inicial
        } else { textVidaRight.text = "Vida: ERR"; }


        // Iniciar IAs
        Debug.Log("Inicializando IA de Luchador Alpha..."); ai1?.InitializeAI();
        Debug.Log("Inicializando IA de Luchador Beta..."); ai2?.InitializeAI();
        Debug.Log("Batalla lista.");
    }

    // ConfigureLuchador ya NO recibe CharacterStats
    void ConfigureLuchador(GameObject instance, string enemyTagToSet, Color teamColorToSet) {
        if (instance == null) { Debug.LogError("Instancia nula en ConfigureLuchador."); return; }

        CharacterData data = instance.GetComponent<CharacterData>();
        if (data == null) { Debug.LogError($"CharacterData no encontrado en {instance.name}", instance); return; }

         // Asegurar que tenga un arma por defecto si no la tiene asignada en el prefab
         if (data.equippedWeapon == null) {
             data.equippedWeapon = defaultWeapon;
              Debug.LogWarning($"Arma no asignada en prefab {instance.name}. Asignando por defecto: {defaultWeapon.name}");
         }

        LuchadorAIController ai = instance.GetComponent<LuchadorAIController>();
        if (ai != null) { ai.enemyTag = enemyTagToSet; } else Debug.LogError($"LuchadorAIController no encontrado en {instance.name}", instance);

        // Configura AIPath usando la stat base de CharacterData
        AIPath aiPath = instance.GetComponent<AIPath>();
        if(aiPath != null) {
             // La velocidad se establecerá en InitializeAI, aquí solo validamos que exista
        } else {
            Debug.LogError($"AIPath no encontrado en {instance.name}", instance);
        }

        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null) { sr.color = teamColorToSet; } else Debug.LogWarning($"SpriteRenderer no encontrado en {instance.name}", instance);

        // Configura el CharacterCombat (ya no necesita stats)
        CharacterCombat combat = instance.GetComponent<CharacterCombat>();
        // combat?.SetTarget() se hará desde la IA

        Debug.Log($"Luchador '{instance.name}' configurado. Tag: {instance.tag}, EnemyTag AI: {enemyTagToSet}");
    }

    // --- UI Updates (Sin cambios) ---
    void UpdateUIHealthLeft(float currentHealth, float maxHealth) { if (textVidaLeft != null) textVidaLeft.text = $"Vida:{currentHealth:F0}"; }
    void UpdateUIHealthRight(float currentHealth, float maxHealth) { if (textVidaRight != null) textVidaRight.text = $"Vida:{currentHealth:F0}"; }

    // --- Update (Detección de Fin de Pelea - SIN CAMBIOS POR AHORA) ---
    // En el siguiente paso, añadiremos aquí la lógica para otorgar XP y volver al menú
    void Update() {
        if (!this.enabled) return;
        bool alphaAlive = (health1 != null && health1.IsAlive());
        bool betaAlive = (health2 != null && health2.IsAlive());
        bool battleOver = false;

        if (!alphaAlive && betaAlive) { Debug.Log($"¡{luchadorInstance2?.name ?? "Luchador Beta"} GANA!"); TriggerCelebration(luchadorInstance2); battleOver = true; }
        else if (!betaAlive && alphaAlive) { Debug.Log($"¡{luchadorInstance1?.name ?? "Luchador Alpha"} GANA!"); TriggerCelebration(luchadorInstance1); battleOver = true; }
        else if (!alphaAlive && !betaAlive) { Debug.Log("¡EMPATE!"); battleOver = true; }

        if (battleOver) {
            Debug.Log("Fin de la batalla. BattleManager desactivado.");
            // TODO: Otorgar XP y volver al menú
            this.enabled = false;
        }
    }

    // --- Celebración (Sin cambios) ---
    void TriggerCelebration(GameObject winner) {
        if (winner != null) {
            LuchadorAIController winnerAI = winner.GetComponent<LuchadorAIController>();
            if (winnerAI != null && winnerAI.enabled) { winnerAI.StartCelebrating(); }
            else { Debug.LogWarning($"No se pudo iniciar celebración para {winner.name}.", winner); }
        } else { Debug.LogWarning("Intento de celebración para ganador nulo."); }
    }
}