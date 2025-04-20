using UnityEngine;
using UnityEngine.SceneManagement; // Necesario si vuelves al menú desde aquí
using System.Collections.Generic; // Necesario para Random.Range si no está ya importado por UnityEngine
using Random = UnityEngine.Random; // Para evitar ambigüedad si hubiera otra clase Random

public class ProgressionManager : MonoBehaviour
{
    [Header("Progression Settings")]
    [Tooltip("XP ganada por victoria.")]
    public float xpPerWin = 50f;
    [Tooltip("Multiplicador para calcular la XP necesaria para el siguiente nivel.")]
    public float xpToLevelMultiplier = 1.5f;
    [Tooltip("Puntos de vida base adicionales ganados por nivel.")]
    public float healthPerLevel = 10f;
    [Tooltip("Daño base adicional ganado por nivel.")]
    public float damagePerLevel = 1.5f;
    [Tooltip("Velocidad base adicional ganada por nivel.")]
    public float speedPerLevel = 0.1f;

    [Header("Data Dependencies")]
    [Tooltip("Asigna el ScriptableObject 'Fist.asset' desde Resources/Weapons aquí.")]
    public WeaponData defaultFistWeapon; // ASIGNA Fist.asset AQUÍ en el Inspector

    private static ProgressionManager instance; // Para Singleton básico

    void Awake()
    {
        // Singleton básico para persistir entre escenas
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            if (defaultFistWeapon == null)
            {
                Debug.LogError("¡Error Crítico! Asigna el 'Default Fist Weapon' (Fist.asset) en el Inspector del ProgressionManager.", this);
                // Considera desactivar el objeto o lanzar una excepción si es vital
                // enabled = false;
            }
        }
        else if (instance != this)
        {
            Debug.LogWarning("Se encontró un ProgressionManager duplicado. Destruyendo este.", gameObject);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Otorga XP a un personaje, comprueba si sube de nivel y guarda los datos actualizados.
    /// </summary>
    /// <param name="winnerCharacterID">El ID del personaje que ganó.</param>
    /// <param name="amount">La cantidad de XP a otorgar.</param>
    public void GrantXP(string winnerCharacterID, float amount)
    {
        if (defaultFistWeapon == null)
        {
            Debug.LogError("ProgressionManager no puede otorgar XP: Default Fist Weapon no está asignado.");
            return;
        }
        if (string.IsNullOrEmpty(winnerCharacterID))
        {
             Debug.LogError("ProgressionManager no puede otorgar XP: winnerCharacterID está vacío.");
             return;
        }
        if (amount <= 0) return; // No hacer nada si la XP es 0 o negativa

        // Carga o crea el SaveData del personaje
        var saveData = SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(winnerCharacterID, defaultFistWeapon);

        if (saveData != null)
        {
            saveData.currentXP += amount;
            Debug.Log($"{winnerCharacterID} ganó {amount} XP. Total: {saveData.currentXP:F0} / {saveData.xpToNextLevel:F0}");

            bool leveledUp = false;
            // Bucle por si se suben múltiples niveles de golpe
            while (saveData.currentXP >= saveData.xpToNextLevel && saveData.level < 99) // Añade un límite de nivel razonable
            {
                LevelUp(saveData); // Modifica el objeto saveData en memoria
                leveledUp = true;
            }

            // Guarda el objeto saveData modificado (con posible nuevo nivel, stats y XP)
            SaveLoadSystem.SaveCharacterSaveData(saveData, winnerCharacterID);

            if (leveledUp)
            {
                Debug.Log($"{winnerCharacterID} ha terminado de subir niveles. Nivel actual: {saveData.level}.");
            }
        }
        else
        {
            Debug.LogError($"No se pudieron cargar o crear los datos para '{winnerCharacterID}' para otorgar XP.");
        }
    }

    /// <summary>
    /// Procesa la subida de nivel de un personaje modificando su objeto CharacterSaveData.
    /// </summary>
    /// <param name="saveData">El objeto CharacterSaveData del personaje a subir de nivel.</param>
    private void LevelUp(SaveLoadSystem.CharacterSaveData saveData)
    {
        if (saveData == null) return;

        // Guarda XP restante y calcula la necesaria para el siguiente
        saveData.currentXP -= saveData.xpToNextLevel;
        saveData.xpToNextLevel = Mathf.RoundToInt(saveData.xpToNextLevel * xpToLevelMultiplier);
        saveData.level++; // Incrementa el nivel

        Debug.Log($"¡{saveData.level}! Nuevo nivel alcanzado. Próximo nivel a los {saveData.xpToNextLevel} XP.");

        // Aplica mejora de stats directamente al objeto saveData
        ApplyRandomStatUpgrade(saveData);

        // TODO (Futuro): Implementar selección aleatoria/ofrecida de Armas/Skills y modificar saveData
        // Ejemplo:
        // List<WeaponData> potentialWeapons = LoadPotentialWeaponsForLevel(saveData.level);
        // WeaponData newWeapon = potentialWeapons[Random.Range(0, potentialWeapons.Count)];
        // saveData.equippedWeaponAssetName = newWeapon.name; // Guarda el nombre del asset
        //
        // List<SkillData> potentialSkills = LoadPotentialSkillsForLevel(saveData.level);
        // SkillData newSkill = potentialSkills[Random.Range(0, potentialSkills.Count)];
        // if (!saveData.learnedSkillAssetNames.Contains(newSkill.name)) {
        //     saveData.learnedSkillAssetNames.Add(newSkill.name); // Guarda el nombre del asset
        // }
    }

    /// <summary>
    /// Aplica una mejora de estadística aleatoria al CharacterSaveData proporcionado.
    /// </summary>
    /// <param name="saveData">El objeto CharacterSaveData a modificar.</param>
    private void ApplyRandomStatUpgrade(SaveLoadSystem.CharacterSaveData saveData)
    {
        if (saveData == null) return;

        int choice = Random.Range(0, 3); // 0: Vida, 1: Daño, 2: Velocidad
        switch (choice)
        {
            case 0:
                saveData.baseMaxHealth += healthPerLevel;
                Debug.Log($"Mejora de Nivel: +{healthPerLevel} Vida Base (Total: {saveData.baseMaxHealth})");
                break;
            case 1:
                saveData.baseAttackDamage += damagePerLevel;
                Debug.Log($"Mejora de Nivel: +{damagePerLevel} Daño Base (Total: {saveData.baseAttackDamage})");
                break;
            case 2:
                saveData.baseMovementSpeed += speedPerLevel;
                Debug.Log($"Mejora de Nivel: +{speedPerLevel} Velocidad Base (Total: {saveData.baseMovementSpeed})");
                break;
        }
    }
}