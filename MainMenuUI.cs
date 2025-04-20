using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;
    public TextMeshProUGUI weaponText;

    [Header("Data")]
    public string playerCharacterID = "PlayerBruto";
    public string defaultOpponentID = "OpponentBruto1";
    [Tooltip("Assign Fist.asset from Resources/Weapons")]
    public WeaponData defaultPlayerWeapon;

    // *** AHORA GUARDA CharacterSaveData ***
    private SaveLoadSystem.CharacterSaveData loadedPlayerSaveData;

    void Start()
    {
        if (defaultPlayerWeapon == null) {
             Debug.LogError("Assign Default Player Weapon in MainMenuUI!"); return;
        }
        LoadAndDisplayPlayerData();
        // Asegura que exista un oponente por defecto
        SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(defaultOpponentID, defaultPlayerWeapon);
    }

    void LoadAndDisplayPlayerData()
    {
        // *** LLAMA A LA FUNCIÓN CORREGIDA ***
        loadedPlayerSaveData = SaveLoadSystem.LoadOrCreateDefaultCharacterSaveData(playerCharacterID, defaultPlayerWeapon);

        // *** USA LOS DATOS DE loadedPlayerSaveData ***
        if (loadedPlayerSaveData != null)
        {
            if(playerNameText) playerNameText.text = playerCharacterID;
            if(levelText) levelText.text = $"Nivel: {loadedPlayerSaveData.level}";
            if(xpText) xpText.text = $"XP: {loadedPlayerSaveData.currentXP:F0} / {loadedPlayerSaveData.xpToNextLevel:F0}";
            // Accede al nombre del arma desde el SaveData, no necesita cargar el asset aquí
            if(weaponText) weaponText.text = $"Arma: {loadedPlayerSaveData.equippedWeaponAssetName ?? "Ninguna"}";
        }
        else
        {
            // ... (mostrar error en UI) ...
            if(playerNameText) playerNameText.text = "ERROR";
             if(levelText) levelText.text = "Nivel: -";
             if(xpText) xpText.text = "XP: - / -";
             if(weaponText) weaponText.text = "Arma: -";
             Debug.LogError("Failed to load or create player data!");
        }
    }

    public void OnStartFightButtonClicked()
    {
        // *** YA NO NECESITA loadedPlayerData aquí ***
        // if (loadedPlayerSaveData == null) { Debug.LogError("Player data not loaded."); return; }

        Debug.Log("Iniciando pelea...");
        PlayerPrefs.SetString("PlayerCharacterIDToLoad", playerCharacterID);
        PlayerPrefs.SetString("OpponentCharacterIDToLoad", defaultOpponentID);
        PlayerPrefs.Save();
        SceneManager.LoadScene("SampleScene"); // Cambia si tu escena se llama diferente
    }

    public void OnQuitButtonClicked()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}