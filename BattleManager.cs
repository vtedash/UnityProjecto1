using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Pathfinding;
using System.Collections;

public class BattleManager : MonoBehaviour
{
    [Header("Setup")]
    public GameObject luchadorPrefab;
    public Transform spawnPoint1; // Jugador (izquierda)
    public Transform spawnPoint2; // Oponente (derecha)

    [Header("Team Colors")]
    public Color playerColor = Color.blue;
    public Color opponentColor = Color.red;

    [Header("UI References")]
    public TextMeshProUGUI textVidaLeft;
    public TextMeshProUGUI textVidaRight;

    // Referencias
    private CharacterData playerDataInstance;
    private CharacterData opponentDataInstance;
    private GameObject luchadorInstance1;
    private GameObject luchadorInstance2;
    private HealthSystem health1;
    private HealthSystem health2;
    // *** CORRECCIÓN: Añadida referencia para ai1 ***
    private LuchadorAIController ai1;
    private LuchadorAIController ai2;

    // IDs
    private string playerCharacterID;
    private string opponentCharacterID;

    private ProgressionManager progressionManager;

    void Start()
    {
        playerCharacterID = PlayerPrefs.GetString("PlayerCharacterIDToLoad", "PlayerBruto");
        opponentCharacterID = PlayerPrefs.GetString("OpponentCharacterIDToLoad", "OpponentBruto1");

        WeaponData defaultFist = Resources.Load<WeaponData>("Weapons/Fist");
        if(defaultFist == null) { Debug.LogError("Fist.asset missing!"); GoToMainMenu(); return; }

        var loadedPlayerSaveData = SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(playerCharacterID, defaultFist);
        var loadedOpponentSaveData = SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(opponentCharacterID, defaultFist);

        if (loadedPlayerSaveData == null || loadedOpponentSaveData == null) { Debug.LogError("Failed to load SaveData."); GoToMainMenu(); return; }

        progressionManager = FindObjectOfType<ProgressionManager>();
        if (progressionManager == null) Debug.LogWarning("ProgressionManager not found.");

        StartBattle(loadedPlayerSaveData, loadedOpponentSaveData);
    }

     Transform CreateSpawnPoint(string name, Vector3 position) {
         GameObject sp = new GameObject(name); sp.transform.position = position;
         sp.transform.SetParent(this.transform); return sp.transform;
     }

    void StartBattle(SaveLoadSystem.CharacterSaveData playerSaveData, SaveLoadSystem.CharacterSaveData opponentSaveData)
    {
        Debug.Log($"Starting battle: {playerCharacterID} vs {opponentCharacterID}");

        if (spawnPoint1 == null) spawnPoint1 = CreateSpawnPoint("SpawnPoint1", new Vector3(-5, 1, 0));
        if (spawnPoint2 == null) spawnPoint2 = CreateSpawnPoint("SpawnPoint2", new Vector3(5, 1, 0));
        if (luchadorPrefab == null) { Debug.LogError("Luchador Prefab missing!"); return; }

        // --- Jugador ---
        luchadorInstance1 = Instantiate(luchadorPrefab, spawnPoint1.position, Quaternion.identity);
        luchadorInstance1.name = $"{playerCharacterID}_Instance";
        luchadorInstance1.tag = "Player";
        playerDataInstance = luchadorInstance1.GetComponent<CharacterData>();
        if (playerDataInstance != null) {
             playerDataInstance.ApplySaveData(playerSaveData);
             ConfigureLuchadorInstance(luchadorInstance1, "Enemy", playerColor); // Su enemigo es "Enemy"
             playerDataInstance.InitializeResourcesAndCooldowns();
        } else { Debug.LogError("Prefab missing CharacterData!", luchadorInstance1); Destroy(luchadorInstance1); return; }
        health1 = luchadorInstance1.GetComponent<HealthSystem>();
        // *** CORRECCIÓN: Obtener y NO desactivar ai1 ***
        ai1 = luchadorInstance1.GetComponent<LuchadorAIController>();
        // La línea "if(playerAI != null) playerAI.enabled = false;" HA SIDO ELIMINADA


         // --- Oponente ---
         luchadorInstance2 = Instantiate(luchadorPrefab, spawnPoint2.position, Quaternion.identity);
         luchadorInstance2.name = $"{opponentCharacterID}_Instance";
         luchadorInstance2.tag = "Enemy";
         opponentDataInstance = luchadorInstance2.GetComponent<CharacterData>();
         if (opponentDataInstance != null) {
              opponentDataInstance.ApplySaveData(opponentSaveData);
              ConfigureLuchadorInstance(luchadorInstance2, "Player", opponentColor); // Su enemigo es "Player"
              opponentDataInstance.InitializeResourcesAndCooldowns();
         } else { Debug.LogError("Prefab missing CharacterData!", luchadorInstance2); Destroy(luchadorInstance2); return; }
         health2 = luchadorInstance2.GetComponent<HealthSystem>();
         ai2 = luchadorInstance2.GetComponent<LuchadorAIController>();


        // Validar componentes (Ahora incluye ai1)
        if (health1 == null || health2 == null || ai1 == null || ai2 == null) {
             Debug.LogError("Error getting components post-instantiate (Health or AI).");
             this.enabled = false; return;
        }

        // Suscribir UI
        if (health1 != null) { health1.OnHealthChanged.AddListener(UpdateUIHealthLeft); UpdateUIHealthLeft(health1.CurrentHealth, health1.MaxHealth); }
        if (health2 != null) { health2.OnHealthChanged.AddListener(UpdateUIHealthRight); UpdateUIHealthRight(health2.CurrentHealth, health2.MaxHealth); }

        // *** CORRECCIÓN: Inicializar AMBAS IAs ***
        ai1?.InitializeAI();
        ai2?.InitializeAI();
        Debug.Log("Battle ready.");
    }

    void ConfigureLuchadorInstance(GameObject instance, string enemyTagToSet, Color teamColorToSet) {
         LuchadorAIController ai = instance.GetComponent<LuchadorAIController>();
         if (ai != null) ai.enemyTag = enemyTagToSet; // Configura a quién debe atacar

         SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
         if (sr != null) sr.color = teamColorToSet;

         CharacterVisuals visuals = instance.GetComponent<CharacterVisuals>();
         visuals?.UpdateWeaponVisuals(); // Actualiza sprite arma (si existe el script/componente)
    }

    // --- UI Updates (SIN CAMBIOS) ---
    void UpdateUIHealthLeft(float currentHealth, float maxHealth) { if (textVidaLeft != null) textVidaLeft.text = $"Vida:{currentHealth:F0}"; }
    void UpdateUIHealthRight(float currentHealth, float maxHealth) { if (textVidaRight != null) textVidaRight.text = $"Vida:{currentHealth:F0}"; }

    // --- Update Fin Pelea (SIN CAMBIOS) ---
    void Update() {
        if (!this.enabled || health1 == null || health2 == null) return;
        bool playerAlive = health1.IsAlive(); bool opponentAlive = health2.IsAlive();
        bool battleOver = false; string winnerID = null;
        if (!playerAlive && opponentAlive) { winnerID = opponentCharacterID; battleOver = true; TriggerCelebration(luchadorInstance2); Debug.Log($"¡{opponentCharacterID} WINS!"); }
        else if (!opponentAlive && playerAlive) { winnerID = playerCharacterID; battleOver = true; TriggerCelebration(luchadorInstance1); Debug.Log($"¡{playerCharacterID} WINS!"); }
        else if (!playerAlive && !opponentAlive) { battleOver = true; Debug.Log("¡DRAW!");}
        if (battleOver) {
            if(winnerID != null && progressionManager != null) { progressionManager.GrantXP(winnerID, progressionManager.xpPerWin); }
            else if (winnerID != null) { Debug.LogWarning("ProgressionManager not found, cannot grant XP."); }
            StartCoroutine(ReturnToMenuAfterDelay(4.0f)); this.enabled = false;
        }
    }

     IEnumerator ReturnToMenuAfterDelay(float delay) { Debug.Log($"Returning to menu in {delay}s..."); yield return new WaitForSeconds(delay); SceneManager.LoadScene("MainMenu"); }
     void TriggerCelebration(GameObject winnerInstance) { if(winnerInstance == null) return; LuchadorAIController ai = winnerInstance.GetComponent<LuchadorAIController>(); if (ai != null) ai.StartCelebrating(); else winnerInstance.GetComponent<CharacterCombat>()?.SetAnimatorTrigger("Celebrate"); } // Permitir celebración aunque la IA esté desactivada
     void GoToMainMenu() { SceneManager.LoadScene("MainMenu"); }
}