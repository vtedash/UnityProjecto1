// File: BattleManager.cs
using UnityEngine;
using System.Collections.Generic; // No es necesario aquí realmente

public class BattleManager : MonoBehaviour
{
    // --- CAMBIO: Nombre de variable y tooltip implícito ---
    public GameObject luchadorPrefab;
    public Transform spawnPoint1;
    public Transform spawnPoint2;
    // --- CAMBIO: Nombres de variables ---
    public CharacterStats statsLuchador1;
    public CharacterStats statsLuchador2;

    [Header("Team Colors")]
    public Color team1Color = Color.blue;
    public Color team2Color = Color.red;

    // --- CAMBIO: Nombres de variables ---
    private GameObject luchadorInstance1;
    private GameObject luchadorInstance2;
    // Cachear HealthSystems para eficiencia
    private HealthSystem health1;
    private HealthSystem health2;

    void Start()
    {
        // --- Validaciones Iniciales ---
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-3, 0, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(3, 0, 0));

        // --- CAMBIO: Mensaje de error ---
        if (statsLuchador1 == null || statsLuchador2 == null)
        {
            Debug.LogError("¡Asigna los CharacterStats para ambos luchadores en el BattleManager!", this);
            enabled = false; return;
        }
        // --- CAMBIO: Mensaje de error y variable ---
        if (luchadorPrefab == null)
        {
             Debug.LogError("¡Asigna el Prefab del Luchador en el BattleManager!", this);
             enabled = false; return;
        }
        // --- Fin Validaciones ---

        StartBattle();
    }

    Transform CreateSpawnPoint(string name, Vector3 position)
    {
        GameObject sp = new GameObject(name);
        sp.transform.position = position;
        sp.transform.SetParent(this.transform);
        return sp.transform;
    }

    void StartBattle()
    {
        Debug.Log("Iniciando batalla...");

        // --- Instanciar y Configurar Luchador 1 ---
        // --- CAMBIO: Variable Prefab y nombre instancia ---
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = "Luchador_Alpha"; // Equipo Player (por defecto)
        luchadorInstance1.tag = "Player"; // Asignar tag para que el otro lo encuentre
        // --- CAMBIO: Llamada a función configuradora y variable stats ---
        ConfigureLuchador(luchadorInstance1, statsLuchador1, "Enemy", team1Color); // Buscará enemigos
        health1 = luchadorInstance1.GetComponent<HealthSystem>(); // Cachear HealthSystem

        // --- Instanciar y Configurar Luchador 2 ---
         // --- CAMBIO: Variable Prefab y nombre instancia ---
        luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
        luchadorInstance2.name = "Luchador_Beta"; // Equipo Enemy
        luchadorInstance2.tag = "Enemy";   // Asignar tag para que el otro lo encuentre
         // --- CAMBIO: Llamada a función configuradora y variable stats ---
        ConfigureLuchador(luchadorInstance2, statsLuchador2, "Player", team2Color); // Buscará Players
        health2 = luchadorInstance2.GetComponent<HealthSystem>(); // Cachear HealthSystem

        // Asegurarse que A* escanee la escena inicial si es necesario
        // if (AstarPath.active != null) AstarPath.active.Scan();
    }

    // --- CAMBIO: Nombre de la función helper ---
    void ConfigureLuchador(GameObject instance, CharacterStats stats, string enemyTagToSet, Color teamColorToSet)
    {
        if (instance == null) return;

        CharacterData data = instance.GetComponent<CharacterData>();
        if (data != null) {
            data.baseStats = stats;
            data.InitializeResourcesAndCooldowns();
        }
        else Debug.LogError($"CharacterData no encontrado en {instance.name}", instance);

        // --- CAMBIO: Tipo de componente a buscar ---
        LuchadorAIController ai = instance.GetComponent<LuchadorAIController>();
        if (ai != null) ai.enemyTag = enemyTagToSet;
        // --- CAMBIO: Mensaje de error ---
        else Debug.LogError($"LuchadorAIController no encontrado en {instance.name}", instance);

        Pathfinding.AIPath aiPath = instance.GetComponent<Pathfinding.AIPath>();
        if(aiPath != null && data != null && data.baseStats != null)
        {
            aiPath.maxSpeed = data.baseStats.movementSpeed;
        } else if (aiPath == null) {
             Debug.LogError($"AIPath no encontrado en {instance.name}", instance);
        }

        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = teamColorToSet;
        else Debug.LogWarning($"SpriteRenderer no encontrado en {instance.name}", instance);
    }


    void Update()
    {
        if (!this.enabled) return;

        // Usar las referencias cacheadas y IsAlive()
        bool alphaAlive = (health1 != null && health1.IsAlive());
        bool betaAlive = (health2 != null && health2.IsAlive());

        bool battleOver = false;

        if (!alphaAlive && betaAlive)
        {
            // --- CAMBIO: Mensaje Log ---
            Debug.Log("¡Luchador Beta (Equipo Enemy) GANA!");
            TriggerCelebration(luchadorInstance2);
            battleOver = true;
        }
        else if (!betaAlive && alphaAlive)
        {
            // --- CAMBIO: Mensaje Log ---
             Debug.Log("¡Luchador Alpha (Equipo Player) GANA!");
             TriggerCelebration(luchadorInstance1);
             battleOver = true;
        }
         else if (!alphaAlive && !betaAlive)
         {
            Debug.Log("¡EMPATE o ambos destruidos!");
            battleOver = true;
         }

         if (battleOver)
         {
             Debug.Log("BattleManager desactivado.");
             this.enabled = false;
         }
    }

    void TriggerCelebration(GameObject winner)
    {
        // Comprobar si el objeto aún existe (podría destruirse justo antes)
        if (winner != null)
        {
            // --- CAMBIO: Tipo de componente a buscar ---
            LuchadorAIController winnerAI = winner.GetComponent<LuchadorAIController>();
            // Comprobar si la IA existe y está habilitada (no debería estarlo si acaba de morir, pero sí si ganó)
            if (winnerAI != null && winnerAI.enabled)
            {
                winnerAI.StartCelebrating();
            }
        }
    }
}