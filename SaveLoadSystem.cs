using UnityEngine;
using System.IO;
using System.Collections.Generic;

public static class SaveLoadSystem
{
    // Clase interna pública para datos serializables
    [System.Serializable]
    public class CharacterSaveData
    {
        public float baseMaxHealth; public float baseMovementSpeed; public float baseAttackDamage;
        public float baseAttackRange; public float baseAttackCooldown; public float baseMaxStamina;
        public float baseStaminaRegenRate; public float baseDashSpeedMult; public float baseDashDuration;
        public float baseDashCost; public float baseDashCooldown; public float baseDashInvulnerabilityDuration;
        public float baseBlockStaminaDrain; public float baseBlockDamageMultiplier; public float baseBlockSuccessStaminaCostMult;
        public float baseBlockSpeedMultiplier; public float baseParryWindow; public float baseParryStunDuration;
        public float baseParryCost; public float baseParryCooldown; public int level;
        public float currentXP; public float xpToNextLevel; public string equippedWeaponAssetName;
        public List<string> learnedSkillAssetNames;
    }

    private static string GetSavePath(string characterID)
    {
        return Path.Combine(Application.persistentDataPath, characterID + ".json");
    }

    // Guarda datos desde un COMPONENTE CharacterData
    public static void SaveCharacter(CharacterData data, string characterID)
    {
        if (data == null) { Debug.LogError($"Save Error (SaveCharacter): Data null for {characterID}"); return; }

        CharacterSaveData saveData = new CharacterSaveData();
        // Copia de MonoBehaviour -> SaveData
        saveData.baseMaxHealth = data.baseMaxHealth; saveData.baseMovementSpeed = data.baseMovementSpeed; saveData.baseAttackDamage = data.baseAttackDamage;
        saveData.baseAttackRange = data.baseAttackRange; saveData.baseAttackCooldown = data.baseAttackCooldown; saveData.baseMaxStamina = data.baseMaxStamina;
        saveData.baseStaminaRegenRate = data.baseStaminaRegenRate; saveData.baseDashSpeedMult = data.baseDashSpeedMult; saveData.baseDashDuration = data.baseDashDuration;
        saveData.baseDashCost = data.baseDashCost; saveData.baseDashCooldown = data.baseDashCooldown; saveData.baseDashInvulnerabilityDuration = data.baseDashInvulnerabilityDuration;
        saveData.baseBlockStaminaDrain = data.baseBlockStaminaDrain; saveData.baseBlockDamageMultiplier = data.baseBlockDamageMultiplier; saveData.baseBlockSuccessStaminaCostMult = data.baseBlockSuccessStaminaCostMult;
        saveData.baseBlockSpeedMultiplier = data.baseBlockSpeedMultiplier; saveData.baseParryWindow = data.baseParryWindow; saveData.baseParryStunDuration = data.baseParryStunDuration;
        saveData.baseParryCost = data.baseParryCost; saveData.baseParryCooldown = data.baseParryCooldown; saveData.level = data.level;
        saveData.currentXP = data.currentXP; saveData.xpToNextLevel = data.xpToNextLevel;
        saveData.equippedWeaponAssetName = data.equippedWeapon?.name; // Usa ?. para seguridad
        saveData.learnedSkillAssetNames = new List<string>();
        if (data.learnedSkills != null) { foreach (SkillData skill in data.learnedSkills) { if (skill != null) saveData.learnedSkillAssetNames.Add(skill.name); } }

        // Llama a la función que guarda el objeto SaveData
        SaveCharacterSaveData(saveData, characterID);
    }

    // NUEVA FUNCIÓN: Guarda un objeto CharacterSaveData directamente a JSON
    public static void SaveCharacterSaveData(CharacterSaveData saveData, string characterID)
    {
        if (saveData == null) { Debug.LogError($"Save Error (SaveCharacterSaveData): SaveData null for {characterID}"); return; }
        string json = JsonUtility.ToJson(saveData, true);
        string path = GetSavePath(characterID);
        try { File.WriteAllText(path, json); /*Debug.Log($"Saved {characterID} to {path}");*/ }
        catch (System.Exception e) { Debug.LogError($"Save Failed for {characterID}: {e.Message}"); }
    }


    // Carga el JSON y devuelve el objeto CharacterSaveData
    public static CharacterSaveData LoadCharacterSaveData(string characterID)
    {
        string path = GetSavePath(characterID);
        if (File.Exists(path)) {
            try {
                string json = File.ReadAllText(path);
                CharacterSaveData loadedSaveData = JsonUtility.FromJson<CharacterSaveData>(json);
                // Debug.Log($"Loaded save data for {characterID}");
                return loadedSaveData;
            } catch (System.Exception e) {
                Debug.LogError($"Load Failed for {characterID}: {e.Message}");
                return null;
            }
        } else { return null; }
    }

    // Carga o crea y guarda datos por defecto, devolviendo CharacterSaveData
    public static CharacterSaveData LoadOrCreateDefaultCharacterSaveData(string characterID, WeaponData defaultWeapon)
    {
         CharacterSaveData loadedData = LoadCharacterSaveData(characterID);
         if (loadedData != null) { return loadedData; }
         else {
             Debug.Log($"Creating default save data for {characterID}");
             CharacterSaveData defaultSaveData = new CharacterSaveData();
             // Configura stats defecto
             defaultSaveData.baseMaxHealth = 100f; defaultSaveData.baseMovementSpeed = 5f; defaultSaveData.baseAttackDamage = 10f;
             defaultSaveData.baseAttackRange = 1f; defaultSaveData.baseAttackCooldown = 1f; defaultSaveData.baseMaxStamina = 100f;
             defaultSaveData.baseStaminaRegenRate = 15f; defaultSaveData.baseDashSpeedMult = 5f; defaultSaveData.baseDashDuration = 0.2f;
             defaultSaveData.baseDashCost = 20f; defaultSaveData.baseDashCooldown = 1.5f; defaultSaveData.baseDashInvulnerabilityDuration = 0.2f;
             defaultSaveData.baseBlockStaminaDrain = 10f; defaultSaveData.baseBlockDamageMultiplier = 0.25f; defaultSaveData.baseBlockSuccessStaminaCostMult = 0.1f;
             defaultSaveData.baseBlockSpeedMultiplier = 0.5f; defaultSaveData.baseParryWindow = 0.15f; defaultSaveData.baseParryStunDuration = 1.0f;
             defaultSaveData.baseParryCost = 30f; defaultSaveData.baseParryCooldown = 2.0f; defaultSaveData.level = 1;
             defaultSaveData.currentXP = 0; defaultSaveData.xpToNextLevel = 100; defaultSaveData.equippedWeaponAssetName = defaultWeapon?.name;
             defaultSaveData.learnedSkillAssetNames = new List<string>();

             // Guarda este SaveData por defecto directamente
             SaveCharacterSaveData(defaultSaveData, characterID); // <<<--- CORRECCIÓN: Llama a guardar el SaveData directamente

             return defaultSaveData;
         }
    }
}