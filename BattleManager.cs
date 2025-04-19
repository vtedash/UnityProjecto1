using UnityEngine;
using System.Collections.Generic; // Necesario si usaras listas en el futuro

public class BattleManager : MonoBehaviour
{
    public GameObject brutoPrefab; // Asigna el Prefab del personaje aquí en el Inspector
    public Transform spawnPoint1; // Un punto de spawn para el equipo 1 (asigna en Inspector o se crea)
    public Transform spawnPoint2; // Un punto de spawn para el equipo 2 (asigna en Inspector o se crea)
    public CharacterStats statsBruto1; // Stats para el primer bruto (asigna en Inspector)
    public CharacterStats statsBruto2; // Stats para el segundo bruto (asigna en Inspector)

    private GameObject brutoInstance1;
    private GameObject brutoInstance2;

    void Start()
    {
        // Crear puntos de spawn si no se asignan en el Inspector
        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-3, 0, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(3, 0, 0));

        // Asegurarse de que las stats estén asignadas antes de iniciar
        if (statsBruto1 == null || statsBruto2 == null)
        {
            Debug.LogError("¡Asigna los CharacterStats para ambos brutos en el BattleManager en el Inspector!", this);
            enabled = false; // No iniciar la batalla si faltan stats
            return;
        }
        if (brutoPrefab == null)
        {
             Debug.LogError("¡Asigna el Prefab del Bruto en el BattleManager en el Inspector!", this);
             enabled = false; // No iniciar si falta el prefab
             return;
        }


        StartBattle();
    }

    // Crea un GameObject vacío como punto de spawn si no se proporcionó uno
    Transform CreateSpawnPoint(string name, Vector3 position)
    {
        GameObject sp = new GameObject(name);
        sp.transform.position = position;
        sp.transform.SetParent(this.transform); // Organizar en la jerarquía
        return sp.transform;
    }

    void StartBattle()
    {
        Debug.Log("Iniciando batalla...");

        // Instanciar Bruto 1
        brutoInstance1 = Instantiate(brutoPrefab, spawnPoint1.position, Quaternion.identity);
        brutoInstance1.name = "Bruto_Alpha";
        // Asegúrate de que la tag "Player" exista en Edit -> Project Settings -> Tags and Layers
        brutoInstance1.tag = "Player";

        // Sobreescribir stats (CharacterData usará estas en su Start)
        CharacterData data1 = brutoInstance1.GetComponent<CharacterData>();
        if(data1 != null)
        {
            data1.baseStats = statsBruto1;
            // data1.InitializeHealth(); // Ya no es necesario si se hace en Start de CharacterData
        }
        else
        {
             Debug.LogError("CharacterData no encontrado en el prefab Bruto instanciado como Bruto_Alpha.", brutoInstance1);
        }


        // Establecer la tag enemiga en la IA
        BrutoAIController ai1 = brutoInstance1.GetComponent<BrutoAIController>();
        if (ai1 != null)
        {
            // Asegúrate de que la tag "Enemy" exista en Edit -> Project Settings -> Tags and Layers
            ai1.enemyTag = "Enemy";
        }
        else
        {
             Debug.LogError("BrutoAIController no encontrado en el prefab Bruto instanciado como Bruto_Alpha.", brutoInstance1);
        }


        // Instanciar Bruto 2
        brutoInstance2 = Instantiate(brutoPrefab, spawnPoint2.position, Quaternion.identity);
        brutoInstance2.name = "Bruto_Beta";
         // Asegúrate de que la tag "Enemy" exista en Edit -> Project Settings -> Tags and Layers
        brutoInstance2.tag = "Enemy";

        // Sobreescribir stats (CharacterData usará estas en su Start)
        CharacterData data2 = brutoInstance2.GetComponent<CharacterData>();
        if(data2 != null)
        {
            data2.baseStats = statsBruto2;
             // data2.InitializeHealth(); // Ya no es necesario si se hace en Start de CharacterData
        }
        else
        {
             Debug.LogError("CharacterData no encontrado en el prefab Bruto instanciado como Bruto_Beta.", brutoInstance2);
        }

        // Establecer la tag enemiga en la IA
        BrutoAIController ai2 = brutoInstance2.GetComponent<BrutoAIController>();
        if (ai2 != null)
        {
            // Asegúrate de que la tag "Player" exista en Edit -> Project Settings -> Tags and Layers
            ai2.enemyTag = "Player";
        }
         else
        {
             Debug.LogError("BrutoAIController no encontrado en el prefab Bruto instanciado como Bruto_Beta.", brutoInstance2);
        }
    }

    // Comprueba el estado de la batalla cada frame
    void Update()
    {
        // Obtener referencias a los HealthSystem de forma segura CADA frame,
        // ya que los GameObjects pueden ser destruidos.
        HealthSystem health1 = GetHealth(brutoInstance1);
        HealthSystem health2 = GetHealth(brutoInstance2);

        // Evaluar condiciones de fin de batalla

        // Bruto 1 fue destruido (es null) Y Bruto 2 todavía existe?
        if (brutoInstance1 == null && brutoInstance2 != null)
        {
            // Adicionalmente, verificar si Bruto 2 está realmente vivo (podría estar a punto de destruirse también)
            if (health2 != null && health2.IsAlive())
            {
                Debug.Log("¡Bruto Beta (Equipo Enemy) GANA!");
                enabled = false; // Desactiva este script de BattleManager para no seguir comprobando ni declarando ganador múltiple
            }
            // Si health2 es null o !IsAlive, podría caer en el empate en el siguiente frame si Bruto 2 también se destruye.
        }
        // Bruto 2 fue destruido (es null) Y Bruto 1 todavía existe?
        else if (brutoInstance2 == null && brutoInstance1 != null)
        {
             // Adicionalmente, verificar si Bruto 1 está realmente vivo
            if (health1 != null && health1.IsAlive())
            {
                 Debug.Log("¡Bruto Alpha (Equipo Player) GANA!");
                 enabled = false; // Desactiva este script
            }
        }
         // Ambos brutos han sido destruidos (son null)?
         else if (brutoInstance1 == null && brutoInstance2 == null)
         {
             // Comprobamos si ya hemos desactivado el script para evitar logs repetidos
             if(this.enabled)
             {
                Debug.Log("¡EMPATE o ambos destruidos!");
                enabled = false; // Desactiva este script
             }
         }
         // Si ninguna de las condiciones anteriores se cumple, la batalla continúa.
    }

    // Helper para obtener referencia a HealthSystem de forma segura
    // Devuelve null si el GameObject es null o no tiene el componente HealthSystem.
    private HealthSystem GetHealth(GameObject instance)
    {
         if (instance != null)
         {
            return instance.GetComponent<HealthSystem>();
         }
         return null;
    }
}