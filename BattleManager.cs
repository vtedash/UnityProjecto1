using UnityEngine;
using System.Collections.Generic; // No es necesario aquí realmente

public class BattleManager : MonoBehaviour
{
    public GameObject brutoPrefab;
    public Transform spawnPoint1;
    public Transform spawnPoint2;
    public CharacterStats statsBruto1;
    public CharacterStats statsBruto2;

    [Header("Team Colors")]
    public Color team1Color = Color.blue;
    public Color team2Color = Color.red;

    private GameObject brutoInstance1;
    private GameObject brutoInstance2;
    // Cachear HealthSystems para eficiencia
    private HealthSystem health1;
    private HealthSystem health2;

    void Start()
    {
        // --- Validaciones Iniciales ---
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-3, 0, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(3, 0, 0));

        if (statsBruto1 == null || statsBruto2 == null)
        {
            Debug.LogError("¡Asigna los CharacterStats para ambos brutos en el BattleManager!", this);
            enabled = false; return;
        }
        if (brutoPrefab == null)
        {
             Debug.LogError("¡Asigna el Prefab del Bruto en el BattleManager!", this);
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

        // --- Instanciar y Configurar Bruto 1 ---
        brutoInstance1 = Instantiate(brutoPrefab, spawnPoint1.position, Quaternion.identity);
        brutoInstance1.name = "Bruto_Alpha"; // Equipo Player (por defecto)
        brutoInstance1.tag = "Player"; // Asignar tag para que el otro lo encuentre
        ConfigureBruto(brutoInstance1, statsBruto1, "Enemy", team1Color); // Buscará enemigos
        health1 = brutoInstance1.GetComponent<HealthSystem>(); // Cachear HealthSystem

        // --- Instanciar y Configurar Bruto 2 ---
        brutoInstance2 = Instantiate(brutoPrefab, spawnPoint2.position, Quaternion.identity);
        brutoInstance2.name = "Bruto_Beta"; // Equipo Enemy
        brutoInstance2.tag = "Enemy";   // Asignar tag para que el otro lo encuentre
        ConfigureBruto(brutoInstance2, statsBruto2, "Player", team2Color); // Buscará Players
        health2 = brutoInstance2.GetComponent<HealthSystem>(); // Cachear HealthSystem

        // Asegurarse que A* escanee la escena inicial si es necesario
        // if (AstarPath.active != null) AstarPath.active.Scan();
    }

    // Función helper para configurar una instancia de Bruto
    void ConfigureBruto(GameObject instance, CharacterStats stats, string enemyTagToSet, Color teamColorToSet)
    {
        if (instance == null) return;

        CharacterData data = instance.GetComponent<CharacterData>();
        if (data != null) {
            data.baseStats = stats;
            data.InitializeResourcesAndCooldowns();
        }
        else Debug.LogError($"CharacterData no encontrado en {instance.name}", instance);

        BrutoAIController ai = instance.GetComponent<BrutoAIController>();
        if (ai != null) ai.enemyTag = enemyTagToSet;
        else Debug.LogError($"BrutoAIController no encontrado en {instance.name}", instance);

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
            Debug.Log("¡Bruto Beta (Equipo Enemy) GANA!");
            TriggerCelebration(brutoInstance2);
            battleOver = true;
        }
        else if (!betaAlive && alphaAlive)
        {
             Debug.Log("¡Bruto Alpha (Equipo Player) GANA!");
             TriggerCelebration(brutoInstance1);
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
            BrutoAIController winnerAI = winner.GetComponent<BrutoAIController>();
            // Comprobar si la IA existe y está habilitada (no debería estarlo si acaba de morir, pero sí si ganó)
            if (winnerAI != null && winnerAI.enabled)
            {
                winnerAI.StartCelebrating();
            }
        }
    }
}