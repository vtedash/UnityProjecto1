using UnityEngine;
using System.Collections.Generic;

public class BattleManager : MonoBehaviour
{
    public GameObject brutoPrefab;
    public Transform spawnPoint1;
    public Transform spawnPoint2;
    public CharacterStats statsBruto1;
    public CharacterStats statsBruto2;

    // --- NUEVAS VARIABLES DE COLOR ---
    [Header("Team Colors")] // Un pequeño encabezado para organizar el Inspector
    public Color team1Color = Color.blue; // Color por defecto para el equipo 1 (Player)
    public Color team2Color = Color.red;  // Color por defecto para el equipo 2 (Enemy)
    // ---------------------------------

    private GameObject brutoInstance1;
    private GameObject brutoInstance2;

    void Start()
    {
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-3, 0, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(3, 0, 0));

        if (statsBruto1 == null || statsBruto2 == null)
        {
            Debug.LogError("¡Asigna los CharacterStats para ambos brutos en el BattleManager en el Inspector!", this);
            enabled = false;
            return;
        }
        if (brutoPrefab == null)
        {
             Debug.LogError("¡Asigna el Prefab del Bruto en el BattleManager en el Inspector!", this);
             enabled = false;
             return;
        }

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

        // --- Instanciar Bruto 1 (Equipo 1 / Player) ---
        brutoInstance1 = Instantiate(brutoPrefab, spawnPoint1.position, Quaternion.identity);
        brutoInstance1.name = "Bruto_Alpha";
        brutoInstance1.tag = "Player";

        CharacterData data1 = brutoInstance1.GetComponent<CharacterData>();
        if(data1 != null) data1.baseStats = statsBruto1;
        else Debug.LogError("CharacterData no encontrado en Bruto_Alpha.", brutoInstance1);

        BrutoAIController ai1 = brutoInstance1.GetComponent<BrutoAIController>();
        if (ai1 != null) ai1.enemyTag = "Enemy";
        else Debug.LogError("BrutoAIController no encontrado en Bruto_Alpha.", brutoInstance1);

        // --- ASIGNAR COLOR AL BRUTO 1 ---
        SpriteRenderer sr1 = brutoInstance1.GetComponent<SpriteRenderer>();
        if (sr1 != null)
        {
            sr1.color = team1Color; // Asigna el color del equipo 1
        }
        else
        {
            Debug.LogWarning("SpriteRenderer no encontrado en Bruto_Alpha. No se pudo asignar color.", brutoInstance1);
        }
        // ----------------------------------


        // --- Instanciar Bruto 2 (Equipo 2 / Enemy) ---
        brutoInstance2 = Instantiate(brutoPrefab, spawnPoint2.position, Quaternion.identity);
        brutoInstance2.name = "Bruto_Beta";
        brutoInstance2.tag = "Enemy";

        CharacterData data2 = brutoInstance2.GetComponent<CharacterData>();
        if(data2 != null) data2.baseStats = statsBruto2;
        else Debug.LogError("CharacterData no encontrado en Bruto_Beta.", brutoInstance2);

        BrutoAIController ai2 = brutoInstance2.GetComponent<BrutoAIController>();
        if (ai2 != null) ai2.enemyTag = "Player";
        else Debug.LogError("BrutoAIController no encontrado en Bruto_Beta.", brutoInstance2);

        // --- ASIGNAR COLOR AL BRUTO 2 ---
        SpriteRenderer sr2 = brutoInstance2.GetComponent<SpriteRenderer>();
        if (sr2 != null)
        {
            sr2.color = team2Color; // Asigna el color del equipo 2
        }
        else
        {
            Debug.LogWarning("SpriteRenderer no encontrado en Bruto_Beta. No se pudo asignar color.", brutoInstance2);
        }
        // ----------------------------------
    }

    void Update()
    {
        HealthSystem health1 = GetHealth(brutoInstance1);
        HealthSystem health2 = GetHealth(brutoInstance2);

        if (brutoInstance1 == null && brutoInstance2 != null)
        {
            if (health2 != null && health2.IsAlive())
            {
                Debug.Log("¡Bruto Beta (Equipo Enemy) GANA!");
                enabled = false;
            }
        }
        else if (brutoInstance2 == null && brutoInstance1 != null)
        {
            if (health1 != null && health1.IsAlive())
            {
                 Debug.Log("¡Bruto Alpha (Equipo Player) GANA!");
                 enabled = false;
            }
        }
         else if (brutoInstance1 == null && brutoInstance2 == null)
         {
             if(this.enabled)
             {
                Debug.Log("¡EMPATE o ambos destruidos!");
                enabled = false;
             }
         }
    }

    private HealthSystem GetHealth(GameObject instance)
    {
         if (instance != null)
         {
            return instance.GetComponent<HealthSystem>();
         }
         return null;
    }
}