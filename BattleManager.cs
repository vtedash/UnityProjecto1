// File: BattleManager.cs
using UnityEngine;
using System.Collections.Generic; // No es necesario aquí realmente

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

    // --- Runtime References ---
    private GameObject luchadorInstance1;
    private GameObject luchadorInstance2;
    private HealthSystem health1; // Cachear HealthSystems para eficiencia
    private HealthSystem health2;
    private LuchadorAIController ai1; // Cachear AI Controllers
    private LuchadorAIController ai2;

    void Start()
    {
        // --- Validaciones Iniciales ---
        // Crear puntos de spawn si no están asignados
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-5, 1, 0)); // Ajusta posiciones si es necesario
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(5, 1, 0));

        // Validar asignación de Stats y Prefab
        if (statsLuchador1 == null || statsLuchador2 == null)
        {
            Debug.LogError("¡Asigna los CharacterStats para ambos luchadores en el BattleManager!", this);
            enabled = false; // Desactivar si falta configuración crítica
            return;
        }
        if (luchadorPrefab == null)
        {
             Debug.LogError("¡Asigna el Prefab del Luchador en el BattleManager!", this);
             enabled = false; // Desactivar si falta configuración crítica
             return;
        }

        // Iniciar la batalla
        StartBattle();
    }

    /// <summary> Helper para crear un punto de spawn si no existe. </summary>
    Transform CreateSpawnPoint(string name, Vector3 position)
    {
        GameObject sp = new GameObject(name);
        sp.transform.position = position;
        sp.transform.SetParent(this.transform); // Hacerlo hijo del BattleManager
        Debug.LogWarning($"Spawn point '{name}' no asignado. Se creó uno en {position}. Ajústalo si es necesario.");
        return sp.transform;
    }

    /// <summary> Inicia la secuencia de la batalla instanciando y configurando los luchadores. </summary>
    void StartBattle()
    {
        Debug.Log("Iniciando batalla...");

        // --- Instanciar y Configurar Luchador 1 (Equipo Player) ---
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = "Luchador_Alpha"; // Nombre para identificarlo
        luchadorInstance1.tag = "Player"; // Asignar etiqueta para que el otro lo encuentre
        // Configurar datos, IA, velocidad y color. Le decimos que busque enemigos con tag "Enemy".
        ConfigureLuchador(luchadorInstance1, statsLuchador1, "Enemy", team1Color);
        // Cachear referencias importantes
        health1 = luchadorInstance1.GetComponent<HealthSystem>();
        ai1 = luchadorInstance1.GetComponent<LuchadorAIController>();

        // --- Instanciar y Configurar Luchador 2 (Equipo Enemy) ---
        luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
        luchadorInstance2.name = "Luchador_Beta"; // Nombre para identificarlo
        luchadorInstance2.tag = "Enemy";   // Asignar etiqueta para que el otro lo encuentre
        // Configurar datos, IA, velocidad y color. Le decimos que busque enemigos con tag "Player".
        ConfigureLuchador(luchadorInstance2, statsLuchador2, "Player", team2Color);
        // Cachear referencias importantes
        health2 = luchadorInstance2.GetComponent<HealthSystem>();
        ai2 = luchadorInstance2.GetComponent<LuchadorAIController>();

        // --- Validar Cacheo ---
        if (health1 == null || health2 == null || ai1 == null || ai2 == null) {
             Debug.LogError("Error al obtener componentes HealthSystem o LuchadorAIController después de instanciar. La batalla podría no funcionar.", this);
             enabled = false;
             return;
        }

        // --- ¡¡IMPORTANTE!! Iniciar las IAs DESPUÉS de que ambos estén configurados ---
        Debug.Log("Inicializando IA de Luchador Alpha...");
        ai1.InitializeAI();
        Debug.Log("Inicializando IA de Luchador Beta...");
        ai2.InitializeAI();

        // Opcional: Forzar un escaneo de A* si la escena cambió dinámicamente
        // if (AstarPath.active != null) AstarPath.active.Scan();
         Debug.Log("Batalla lista.");
    }

    /// <summary> Función helper para configurar una instancia de Luchador. </summary>
    void ConfigureLuchador(GameObject instance, CharacterStats stats, string enemyTagToSet, Color teamColorToSet)
    {
        if (instance == null)
        {
             Debug.LogError("Se intentó configurar una instancia nula de Luchador.");
             return;
        }

        // 1. Configurar Datos del Personaje
        CharacterData data = instance.GetComponent<CharacterData>();
        if (data != null)
        {
            data.baseStats = stats; // Asignar los ScriptableObject Stats
            // La inicialización de vida/stamina se hace ahora en CharacterData.Start()
            // data.InitializeResourcesAndCooldowns(); // Ya no es necesario aquí si Start lo hace
        }
        else Debug.LogError($"CharacterData no encontrado en {instance.name}", instance);

        // 2. Configurar Controlador de IA
        LuchadorAIController ai = instance.GetComponent<LuchadorAIController>();
        if (ai != null)
        {
            ai.enemyTag = enemyTagToSet; // Decirle a quién buscar
        }
        else Debug.LogError($"LuchadorAIController no encontrado en {instance.name}", instance);

        // 3. Configurar Velocidad de AIPath (basado en stats)
        Pathfinding.AIPath aiPath = instance.GetComponent<Pathfinding.AIPath>();
        if(aiPath != null && data != null && data.baseStats != null)
        {
            aiPath.maxSpeed = data.baseStats.movementSpeed; // Asegurar que AIPath conozca la velocidad correcta
        }
        else if (aiPath == null)
        {
             Debug.LogError($"AIPath no encontrado en {instance.name}", instance);
        }
        else if (data == null || data.baseStats == null)
        {
             Debug.LogWarning($"No se pudo configurar AIPath.maxSpeed para {instance.name} debido a falta de CharacterData/BaseStats.", instance);
        }


        // 4. Configurar Apariencia (Color)
        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = teamColorToSet; // Aplicar color del equipo
        }
        else Debug.LogWarning($"SpriteRenderer no encontrado en {instance.name}", instance);

         Debug.Log($"Luchador '{instance.name}' configurado. Tag: {instance.tag}, EnemyTag AI: {enemyTagToSet}");
    }


    /// <summary> Verifica el estado de la batalla cada frame. </summary>
    void Update()
    {
        // No hacer nada si el BattleManager está desactivado (ej. después de que termine la batalla)
        if (!this.enabled) return;

        // Comprobar si los luchadores todavía existen y están vivos (usando referencias cacheadas)
        bool alphaAlive = (health1 != null && health1.IsAlive());
        bool betaAlive = (health2 != null && health2.IsAlive());

        bool battleOver = false;

        // Determinar ganador
        if (!alphaAlive && betaAlive) // Alpha murió, Beta vive
        {
            Debug.Log($"¡{luchadorInstance2?.name ?? "Luchador Beta"} (Equipo Enemy) GANA!");
            TriggerCelebration(luchadorInstance2);
            battleOver = true;
        }
        else if (!betaAlive && alphaAlive) // Beta murió, Alpha vive
        {
             Debug.Log($"¡{luchadorInstance1?.name ?? "Luchador Alpha"} (Equipo Player) GANA!");
             TriggerCelebration(luchadorInstance1);
             battleOver = true;
        }
         else if (!alphaAlive && !betaAlive) // Ambos murieron (empate o error)
         {
            Debug.Log("¡EMPATE o ambos destruidos!");
            battleOver = true;
         }

         // Si la batalla terminó, desactivar este manager
         if (battleOver)
         {
             Debug.Log("Fin de la batalla. BattleManager desactivado.");
             this.enabled = false;
         }
    }

    /// <summary> Dispara la animación/estado de celebración en el ganador. </summary>
    void TriggerCelebration(GameObject winner)
    {
        // Comprobar si el objeto ganador aún existe (podría destruirse justo antes)
        if (winner != null)
        {
            LuchadorAIController winnerAI = winner.GetComponent<LuchadorAIController>();
            // Comprobar si la IA existe y está habilitada (no debería estarlo si acaba de morir, pero sí si ganó)
            if (winnerAI != null && winnerAI.enabled)
            {
                winnerAI.StartCelebrating();
            }
             else if (winnerAI == null)
             {
                 Debug.LogWarning($"El ganador {winner.name} no tiene LuchadorAIController para celebrar.", winner);
             }
             else if (!winnerAI.enabled) {
                 // Esto no debería pasar si ganó, pero por si acaso.
                 Debug.LogWarning($"La IA del ganador {winner.name} está desactivada, no puede celebrar.", winner);
             }
        } else {
             Debug.LogWarning("Se intentó disparar celebración, pero el objeto ganador ya no existe.");
        }
    }
}