using UnityEngine;
using System.Collections.Generic;

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
        brutoInstance1.name = "Bruto_Alpha";
        brutoInstance1.tag = "Player";
        ConfigureBruto(brutoInstance1, statsBruto1, "Enemy", team1Color);

        // --- Instanciar y Configurar Bruto 2 ---
        brutoInstance2 = Instantiate(brutoPrefab, spawnPoint2.position, Quaternion.identity);
        brutoInstance2.name = "Bruto_Beta";
        brutoInstance2.tag = "Enemy";
        ConfigureBruto(brutoInstance2, statsBruto2, "Player", team2Color);

        // Asegurarse que A* escanee la escena inicial si no lo ha hecho ya
        // Esto es útil si las plataformas se generan dinámicamente
        // if (AstarPath.active != null) AstarPath.active.Scan();
    }

    // Función helper para configurar una instancia de Bruto
    void ConfigureBruto(GameObject instance, CharacterStats stats, string enemyTagToSet, Color teamColorToSet)
    {
        if (instance == null) return;

        // Asignar Stats
        CharacterData data = instance.GetComponent<CharacterData>();
        if (data != null) data.baseStats = stats;
        else Debug.LogError("CharacterData no encontrado en " + instance.name, instance);

        // Configurar IA
        BrutoAIController ai = instance.GetComponent<BrutoAIController>();
        if (ai != null) ai.enemyTag = enemyTagToSet;
        else Debug.LogError("BrutoAIController no encontrado en " + instance.name, instance);

        // Configurar AIPath con la velocidad de las stats
        Pathfinding.AIPath aiPath = instance.GetComponent<Pathfinding.AIPath>();
        if(aiPath != null && data != null && data.baseStats != null)
        {
            aiPath.maxSpeed = data.baseStats.movementSpeed;
        } else if (aiPath == null) {
             Debug.LogError("AIPath no encontrado en " + instance.name, instance);
        }

        // Asignar Color
        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = teamColorToSet;
        else Debug.LogWarning("SpriteRenderer no encontrado en " + instance.name, instance);
    }


    void Update()
    {
        // Solo continuar si el script está habilitado (para evitar acciones después de terminar)
        if (!this.enabled) return;

        HealthSystem health1 = GetHealth(brutoInstance1);
        HealthSystem health2 = GetHealth(brutoInstance2);

        // --- Comprobar condiciones de victoria/empate ---
        bool winnerFound = false;

        // ¿Ganó Bruto Beta (Equipo Enemy)?
        if (brutoInstance1 == null && brutoInstance2 != null)
        {
            if (health2 != null && health2.IsAlive())
            {
                Debug.Log("¡Bruto Beta (Equipo Enemy) GANA!");
                TriggerCelebration(brutoInstance2);
                winnerFound = true;
            }
        }
        // ¿Ganó Bruto Alpha (Equipo Player)?
        else if (brutoInstance2 == null && brutoInstance1 != null)
        {
            if (health1 != null && health1.IsAlive())
            {
                 Debug.Log("¡Bruto Alpha (Equipo Player) GANA!");
                 TriggerCelebration(brutoInstance1);
                 winnerFound = true;
            }
        }
         // ¿Ambos destruidos?
         else if (brutoInstance1 == null && brutoInstance2 == null)
         {
            Debug.Log("¡EMPATE o ambos destruidos!");
            winnerFound = true; // Considerar empate como fin
         }

         // Si se encontró un ganador o empate, desactivar este manager
         if (winnerFound)
         {
             enabled = false;
         }
         // --- Fin comprobación ---
    }

    // Llama a la celebración en el IA del ganador
    void TriggerCelebration(GameObject winner)
    {
        if (winner != null)
        {
            BrutoAIController winnerAI = winner.GetComponent<BrutoAIController>();
            if (winnerAI != null)
            {
                winnerAI.StartCelebrating();
            }
        }
    }

    // Helper para obtener HealthSystem de forma segura
    private HealthSystem GetHealth(GameObject instance)
    {
         if (instance != null) return instance.GetComponent<HealthSystem>();
         return null;
    }
}