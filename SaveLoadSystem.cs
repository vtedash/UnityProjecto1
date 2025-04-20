using UnityEngine;
using System.IO; // Necesario para trabajar con archivos
using System.Collections.Generic; // Necesario para listas

// Clase estática: No se puede instanciar, se accede directamente a sus métodos.
public static class SaveLoadSystem
{
    // Clase interna para guardar/cargar datos fácilmente con JsonUtility
    // JsonUtility no serializa directamente MonoBehaviours (como CharacterData)
    // ni ScriptableObjects referenciados directamente dentro de ellos de forma fiable.
    [System.Serializable]
    private class CharacterSaveData
    {
        // Stats Base
        public float baseMaxHealth;
        public float baseMovementSpeed;
        public float baseAttackDamage;
        public float baseAttackRange;
        public float baseAttackCooldown;
        public float baseMaxStamina;
        public float baseStaminaRegenRate;
        public float baseDashSpeedMult;
        public float baseDashDuration;
        public float baseDashCost;
        public float baseDashCooldown;
        public float baseDashInvulnerabilityDuration;
        public float baseBlockStaminaDrain;
        public float baseBlockDamageMultiplier;
        public float baseBlockSuccessStaminaCostMult;
        public float baseBlockSpeedMultiplier;
        public float baseParryWindow;
        public float baseParryStunDuration;
        public float baseParryCost;
        public float baseParryCooldown;

        // Progresión
        public int level;
        public float currentXP;
        public float xpToNextLevel;

        // Equipo y Habilidades (Guardamos los NOMBRES de los assets)
        public string equippedWeaponAssetName; // Nombre del archivo .asset del arma
        public List<string> learnedSkillAssetNames; // Lista de nombres de archivos .asset de skills
    }

    // Devuelve la ruta completa del archivo de guardado
    private static string GetSavePath(string characterID)
    {
        // Application.persistentDataPath es una carpeta segura y estándar para guardar datos
        return Path.Combine(Application.persistentDataPath, characterID + ".json");
    }

    // Guarda los datos de un CharacterData a un archivo JSON
    public static void SaveCharacter(CharacterData data, string characterID)
    {
        if (data == null)
        {
            Debug.LogError($"Cannot save character {characterID}: CharacterData is null!");
            return;
        }

        CharacterSaveData saveData = new CharacterSaveData();

        // Copia los datos del CharacterData al objeto serializable
        saveData.baseMaxHealth = data.baseMaxHealth;
        saveData.baseMovementSpeed = data.baseMovementSpeed;
        saveData.baseAttackDamage = data.baseAttackDamage;
        saveData.baseAttackRange = data.baseAttackRange;
        saveData.baseAttackCooldown = data.baseAttackCooldown;
        saveData.baseMaxStamina = data.baseMaxStamina;
        saveData.baseStaminaRegenRate = data.baseStaminaRegenRate;
        saveData.baseDashSpeedMult = data.baseDashSpeedMult;
        saveData.baseDashDuration = data.baseDashDuration;
        saveData.baseDashCost = data.baseDashCost;
        saveData.baseDashCooldown = data.baseDashCooldown;
        saveData.baseDashInvulnerabilityDuration = data.baseDashInvulnerabilityDuration;
        saveData.baseBlockStaminaDrain = data.baseBlockStaminaDrain;
        saveData.baseBlockDamageMultiplier = data.baseBlockDamageMultiplier;
        saveData.baseBlockSuccessStaminaCostMult = data.baseBlockSuccessStaminaCostMult;
        saveData.baseBlockSpeedMultiplier = data.baseBlockSpeedMultiplier;
        saveData.baseParryWindow = data.baseParryWindow;
        saveData.baseParryStunDuration = data.baseParryStunDuration;
        saveData.baseParryCost = data.baseParryCost;
        saveData.baseParryCooldown = data.baseParryCooldown;
        saveData.level = data.level;
        saveData.currentXP = data.currentXP;
        saveData.xpToNextLevel = data.xpToNextLevel;

        // Guarda el nombre del asset del arma equipada
        saveData.equippedWeaponAssetName = data.equippedWeapon != null ? data.equippedWeapon.name : null;

        // Guarda los nombres de los assets de las habilidades aprendidas
        saveData.learnedSkillAssetNames = new List<string>();
        foreach (SkillData skill in data.learnedSkills)
        {
            if (skill != null)
            {
                saveData.learnedSkillAssetNames.Add(skill.name);
            }
        }

        // Convierte a JSON y guarda en archivo
        string json = JsonUtility.ToJson(saveData, true); // true para formato legible
        string path = GetSavePath(characterID);
        try
        {
            File.WriteAllText(path, json);
            Debug.Log($"Saved character {characterID} to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save character {characterID} to {path}: {e.Message}");
        }
    }

    // Carga los datos de un personaje desde un archivo JSON
    // Devuelve un CharacterData TEMPORAL con los datos cargados.
    // ¡ESTE NO ES EL COMPONENTE DE LA ESCENA!
    public static CharacterData LoadCharacterData(string characterID)
    {
        string path = GetSavePath(characterID);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                CharacterSaveData loadedSaveData = JsonUtility.FromJson<CharacterSaveData>(json);

                // Crea una INSTANCIA TEMPORAL de CharacterData (no es un componente)
                // Usamos ScriptableObject.CreateInstance para esto, aunque sea MonoBehaviour,
                // porque no queremos añadirlo a la escena aquí.
                CharacterData loadedCharacterData = ScriptableObject.CreateInstance<CharacterData>();

                // Copia los datos del objeto cargado a la instancia temporal
                loadedCharacterData.baseMaxHealth = loadedSaveData.baseMaxHealth;
                loadedCharacterData.baseMovementSpeed = loadedSaveData.baseMovementSpeed;
                loadedCharacterData.baseAttackDamage = loadedSaveData.baseAttackDamage;
                // ... copia TODAS las demás stats base ...
                loadedCharacterData.baseAttackRange = loadedSaveData.baseAttackRange;
                 loadedCharacterData.baseAttackCooldown = loadedSaveData.baseAttackCooldown;
                 loadedCharacterData.baseMaxStamina = loadedSaveData.baseMaxStamina;
                 loadedCharacterData.baseStaminaRegenRate = loadedSaveData.baseStaminaRegenRate;
                 loadedCharacterData.baseDashSpeedMult = loadedSaveData.baseDashSpeedMult;
                 loadedCharacterData.baseDashDuration = loadedSaveData.baseDashDuration;
                 loadedCharacterData.baseDashCost = loadedSaveData.baseDashCost;
                 loadedCharacterData.baseDashCooldown = loadedSaveData.baseDashCooldown;
                 loadedCharacterData.baseDashInvulnerabilityDuration = loadedSaveData.baseDashInvulnerabilityDuration;
                 loadedCharacterData.baseBlockStaminaDrain = loadedSaveData.baseBlockStaminaDrain;
                 loadedCharacterData.baseBlockDamageMultiplier = loadedSaveData.baseBlockDamageMultiplier;
                 loadedCharacterData.baseBlockSuccessStaminaCostMult = loadedSaveData.baseBlockSuccessStaminaCostMult;
                 loadedCharacterData.baseBlockSpeedMultiplier = loadedSaveData.baseBlockSpeedMultiplier;
                 loadedCharacterData.baseParryWindow = loadedSaveData.baseParryWindow;
                 loadedCharacterData.baseParryStunDuration = loadedSaveData.baseParryStunDuration;
                 loadedCharacterData.baseParryCost = loadedSaveData.baseParryCost;
                 loadedCharacterData.baseParryCooldown = loadedSaveData.baseParryCooldown;

                loadedCharacterData.level = loadedSaveData.level;
                loadedCharacterData.currentXP = loadedSaveData.currentXP;
                loadedCharacterData.xpToNextLevel = loadedSaveData.xpToNextLevel;

                // Carga el asset del arma usando el nombre guardado
                if (!string.IsNullOrEmpty(loadedSaveData.equippedWeaponAssetName))
                {
                    // Resources.Load busca en CUALQUIER carpeta llamada "Resources" dentro de Assets
                    // ¡Asegúrate de que tus WeaponData assets estén en una carpeta "Resources/Weapons"!
                    loadedCharacterData.equippedWeapon = Resources.Load<WeaponData>("Weapons/" + loadedSaveData.equippedWeaponAssetName);
                    if (loadedCharacterData.equippedWeapon == null)
                    {
                        Debug.LogWarning($"Could not load weapon asset named '{loadedSaveData.equippedWeaponAssetName}' from Resources/Weapons folder for character {characterID}.");
                    }
                }

                // Carga los assets de las habilidades aprendidas
                loadedCharacterData.learnedSkills = new List<SkillData>();
                foreach (string skillName in loadedSaveData.learnedSkillAssetNames)
                {
                    // ¡Asegúrate de que tus SkillData assets estén en una carpeta "Resources/Skills"!
                    SkillData skill = Resources.Load<SkillData>("Skills/" + skillName);
                    if (skill != null)
                    {
                        loadedCharacterData.learnedSkills.Add(skill);
                    }
                    else
                    {
                        Debug.LogWarning($"Could not load skill asset named '{skillName}' from Resources/Skills folder for character {characterID}.");
                    }
                }

                Debug.Log($"Loaded character data for {characterID}");
                return loadedCharacterData; // Devuelve la instancia temporal con los datos
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load character {characterID} from {path}: {e.Message}");
                return null;
            }
        }
        else
        {
            // Archivo no encontrado, no es necesariamente un error la primera vez
            // Debug.LogWarning($"Save file not found for {characterID} at {path}. Returning null.");
            return null;
        }
    }

    // Carga datos o crea un personaje por defecto si no existe archivo
    public static CharacterData LoadOrCreateDefaultCharacterData(string characterID, WeaponData defaultWeapon)
    {
         CharacterData loadedData = LoadCharacterData(characterID);
         if (loadedData != null)
         {
             return loadedData; // Devuelve los datos cargados
         }
         else
         {
             Debug.Log($"Creating default character data for {characterID}");
             // Crea una nueva instancia TEMPORAL de CharacterData
             CharacterData defaultData = ScriptableObject.CreateInstance<CharacterData>();

             // --- Configura stats iniciales POR DEFECTO ---
             defaultData.baseMaxHealth = 100f;
             defaultData.baseMovementSpeed = 5f;
             defaultData.baseAttackDamage = 10f;
             defaultData.baseAttackRange = 1f;
             defaultData.baseAttackCooldown = 1f;
             defaultData.baseMaxStamina = 100f;
             defaultData.baseStaminaRegenRate = 15f;
             defaultData.baseDashSpeedMult = 5f;
             defaultData.baseDashDuration = 0.2f;
             defaultData.baseDashCost = 20f;
             defaultData.baseDashCooldown = 1.5f;
             defaultData.baseDashInvulnerabilityDuration = 0.2f;
             defaultData.baseBlockStaminaDrain = 10f;
             defaultData.baseBlockDamageMultiplier = 0.25f;
             defaultData.baseBlockSuccessStaminaCostMult = 0.1f;
             defaultData.baseBlockSpeedMultiplier = 0.5f;
             defaultData.baseParryWindow = 0.15f;
             defaultData.baseParryStunDuration = 1.0f;
             defaultData.baseParryCost = 30f;
             defaultData.baseParryCooldown = 2.0f;
             defaultData.level = 1;
             defaultData.currentXP = 0;
             defaultData.xpToNextLevel = 100;
             defaultData.equippedWeapon = defaultWeapon; // Asigna el arma por defecto
             defaultData.learnedSkills = new List<SkillData>(); // Empieza sin skills

             // Guarda este nuevo personaje por defecto para la próxima vez
             SaveCharacter(defaultData, characterID);
             return defaultData; // Devuelve los datos por defecto
         }
    }
}