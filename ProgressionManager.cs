using UnityEngine;
using UnityEngine.SceneManagement; // Necesario si vuelves al menú desde aquí

public class ProgressionManager : MonoBehaviour
{
    [Header("Progression Settings")]
    public float xpPerWin = 50f;
    public float xpToLevelMultiplier = 1.5f;
    public float healthPerLevel = 10f;
    public float damagePerLevel = 1.5f;
    public float speedPerLevel = 0.1f;

     [Header("Data Dependencies")]
     [Tooltip("Assign Fist.asset from Resources/Weapons here")]
    public WeaponData defaultFistWeapon; // ASIGNA Fist.asset AQUÍ en el Inspector

    private static ProgressionManager instance; // Para Singleton básico

    void Awake()
    {
         // Singleton básico para persistir entre escenas
         if (instance == null) {
             instance = this;
             DontDestroyOnLoad(gameObject);
             if(defaultFistWeapon == null) Debug.LogError("Assign Default Fist Weapon in ProgressionManager!", this);
         } else if (instance != this) {
             Debug.LogWarning("Multiple ProgressionManagers found! Destroying this duplicate.");
             Destroy(gameObject);
         }
    }

    // Llamado por BattleManager
    using UnityEngine;
using UnityEngine.SceneManagement;

public class ProgressionManager : MonoBehaviour
{
    // ... (Variables públicas SIN CAMBIOS) ...
    [Header("Progression Settings")]
    public float xpPerWin = 50f;
    public float xpToLevelMultiplier = 1.5f;
    public float healthPerLevel = 10f;
    public float damagePerLevel = 1.5f;
    public float speedPerLevel = 0.1f;
     [Header("Data Dependencies")]
     [Tooltip("Assign Fist.asset from Resources/Weapons here")]
    public WeaponData defaultFistWeapon;

    private static ProgressionManager instance;

    void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); }
        else if (instance != this) { Destroy(gameObject); }
        if(defaultFistWeapon == null) Debug.LogError("Assign Default Fist Weapon in ProgressionManager!");
    }

    // Llamado por BattleManager
    public void GrantXP(string winnerCharacterID, float amount)
    {
        if(defaultFistWeapon == null) { Debug.LogError("ProgressionManager: Default Fist Weapon missing!"); return; }

        // Carga SaveData
        var saveData = SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(winnerCharacterID, defaultFistWeapon);
        if (saveData != null)
        {
            saveData.currentXP += amount;
            // Debug.Log($"{winnerCharacterID} gained {amount} XP. Total: {saveData.currentXP:F0}/{saveData.xpToNextLevel:F0}"); // Log comentado

            bool leveledUp = false;
            while (saveData.currentXP >= saveData.xpToNextLevel)
            {
                LevelUp(saveData); // Modifica el objeto saveData
                leveledUp = true;
            }

            // *** CORRECCIÓN: Guarda el saveData modificado directamente ***
            SaveLoadSystem.SaveCharacterSaveData(saveData, winnerCharacterID);
            // -------------------------------------------------------------

            if(leveledUp) { Debug.Log($"{winnerCharacterID} finished leveling up! Now level {saveData.level}."); }

        } else { Debug.LogError($"Could not load/create data for {winnerCharacterID} to grant XP."); }
    }

    // Modifica el SaveData (SIN CAMBIOS INTERNOS)
    private void LevelUp(SaveLoadSystem.CharacterSaveData saveData)
    {
        saveData.level++;
        saveData.currentXP -= saveData.xpToNextLevel;
        saveData.xpToNextLevel = Mathf.RoundToInt(saveData.xpToNextLevel * xpToLevelMultiplier);
        Debug.Log($"Level Up to Level {saveData.level}! Next level at {saveData.xpToNextLevel} XP.");
        ApplyRandomStatUpgrade(saveData);
        // TODO: Selección de mejoras reales
    }

    // Modifica el SaveData (SIN CAMBIOS INTERNOS)
    private void ApplyRandomStatUpgrade(SaveLoadSystem.CharacterSaveData saveData)
    {
        int choice = Random.Range(0, 3);
        switch (choice) {
            case 0: saveData.baseMaxHealth += healthPerLevel; Debug.Log($"Upgrade: +{healthPerLevel} HP (Now: {saveData.baseMaxHealth})"); break;
            case 1: saveData.baseAttackDamage += damagePerLevel; Debug.Log($"Upgrade: +{damagePerLevel} Dmg (Now: {saveData.baseAttackDamage})"); break;
            case 2: saveData.baseMovementSpeed += speedPerLevel; Debug.Log($"Upgrade: +{speedPerLevel} Spd (Now: {saveData.baseMovementSpeed})"); break;
        }
    }
}

    // Modifica el objeto SaveData directamente
    private void LevelUp(SaveLoadSystem.CharacterSaveData saveData) // Acepta CharacterSaveData
    {
        saveData.level++;
        saveData.currentXP -= saveData.xpToNextLevel;
        saveData.xpToNextLevel = Mathf.RoundToInt(saveData.xpToNextLevel * xpToLevelMultiplier);

        Debug.Log($"Level Up to Level {saveData.level}! Next level at {saveData.xpToNextLevel} XP.");

        // Aplica mejora de stats al SaveData
        ApplyRandomStatUpgrade(saveData);

        // TODO: Implementar selección aleatoria de Armas/Skills y modificar saveData
        // saveData.equippedWeaponAssetName = newWeapon.name;
        // saveData.learnedSkillAssetNames.Add(newSkill.name);
    }

    // Modifica el objeto SaveData directamente
    private void ApplyRandomStatUpgrade(SaveLoadSystem.CharacterSaveData saveData) // Acepta CharacterSaveData
    {
        int choice = Random.Range(0, 3);
        switch (choice) {
            case 0: saveData.baseMaxHealth += healthPerLevel; Debug.Log($"Upgrade: +{healthPerLevel} HP (Now: {saveData.baseMaxHealth})"); break;
            case 1: saveData.baseAttackDamage += damagePerLevel; Debug.Log($"Upgrade: +{damagePerLevel} Dmg (Now: {saveData.baseAttackDamage})"); break;
            case 2: saveData.baseMovementSpeed += speedPerLevel; Debug.Log($"Upgrade: +{speedPerLevel} Spd (Now: {saveData.baseMovementSpeed})"); break;
        }
    }
}