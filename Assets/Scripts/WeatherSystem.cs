using UnityEngine;
using Enviro;

public class WeatherSystem : MonoBehaviour
{
    public enum WeatherState
    {
        Sunny,
        Cloudy,
        Rain,
        Snow
    }

    [Header("Refs")]
    public GpuParticles gpuParticles;
    public GameObject cloudRoot;
    public ParticleSystem cloudShuriken;

    [Header("Current State")]
    public WeatherState currentState = WeatherState.Sunny;

    [Header("Enviro Sync")]
    [Tooltip("When enabled, this WeatherSystem mirrors Enviro weather changes.")]
    public bool syncWithEnviro = true;

    [Header("Debug Keys")]
    [Tooltip("Only use these if you want to force WeatherSystem visuals manually. If Enviro is your source of truth, leave this off.")]
    public bool enableDebugKeys = false;
    public KeyCode sunnyKey = KeyCode.Alpha1;
    public KeyCode cloudyKey = KeyCode.Alpha2;
    public KeyCode rainKey = KeyCode.Alpha3;
    public KeyCode snowKey = KeyCode.Alpha4;

    void Start()
    {
        // Apply inspector default until Enviro fires its first weather event.
        ApplyState(currentState, true);
    }

    void OnEnable()
    {
        if (!syncWithEnviro)
            return;

        if (EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged += OnEnviroWeatherChanged;
            Debug.Log("[WeatherSystem] Subscribed to Enviro weather events.");
        }
        else
        {
            Debug.LogWarning("[WeatherSystem] Enviro sync enabled, but EnviroManager.instance was not found.");
        }
    }

    void OnDisable()
    {
        if (syncWithEnviro && EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged -= OnEnviroWeatherChanged;
            Debug.Log("[WeatherSystem] Unsubscribed from Enviro weather events.");
        }
    }

    void Update()
    {
        if (!enableDebugKeys) return;

        if (Input.GetKeyDown(sunnyKey))
            SetSunny();

        if (Input.GetKeyDown(cloudyKey))
            SetCloudy();

        if (Input.GetKeyDown(rainKey))
            SetRain();

        if (Input.GetKeyDown(snowKey))
            SetSnow();
    }

    private void OnEnviroWeatherChanged(EnviroWeatherType currentWeatherType)
    {
        ApplyState(MapEnviroWeather(currentWeatherType));

        Debug.Log($"[WeatherSystem] Enviro weather changed to '{(currentWeatherType != null ? currentWeatherType.name : "N/A")}', mapped to '{currentState}'.");
    }

    private WeatherState MapEnviroWeather(EnviroWeatherType currentWeatherType)
    {
        if (currentWeatherType == null)
            return WeatherState.Sunny;

        switch (currentWeatherType.name)
        {
            case "Clear Sky":
                return WeatherState.Sunny;

            case "Cloudy 1":
            case "Cloudy 2":
            case "Cloudy 3":
            case "Foggy":
                return WeatherState.Cloudy;

            case "Rain":
                return WeatherState.Rain;

            case "Snow":
                return WeatherState.Snow;

            default:
                Debug.LogWarning($"[WeatherSystem] Unhandled Enviro weather type '{currentWeatherType.name}'. Falling back to Sunny visuals.");
                return WeatherState.Sunny;
        }
    }

    public void SetSunny()
    {
        ApplyState(WeatherState.Sunny);
    }

    public void SetCloudy()
    {
        ApplyState(WeatherState.Cloudy);
    }

    public void SetRain()
    {
        ApplyState(WeatherState.Rain);
    }

    public void SetSnow()
    {
        ApplyState(WeatherState.Snow);
    }

    public void ApplyState(WeatherState newState, bool forceClear = false)
    {
        WeatherState previousState = currentState;
        currentState = newState;

        bool cloudsOn = (newState != WeatherState.Sunny);

        if (cloudRoot != null)
            cloudRoot.SetActive(cloudsOn);

        if (cloudShuriken != null)
        {
            if (cloudsOn)
            {
                if (!cloudShuriken.isPlaying)
                    cloudShuriken.Play();
            }
            else
            {
                cloudShuriken.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (gpuParticles == null) return;

        bool switchingBetweenRainAndSnow =
            (previousState == WeatherState.Rain && newState == WeatherState.Snow) ||
            (previousState == WeatherState.Snow && newState == WeatherState.Rain);

        switch (newState)
        {
            case WeatherState.Sunny:
                gpuParticles.SetVisible(false);
                gpuParticles.StopEmission();
                gpuParticles.ClearParticlesNow();
                break;

            case WeatherState.Cloudy:
                gpuParticles.SetVisible(false);
                gpuParticles.StopEmission();
                gpuParticles.ClearParticlesNow();
                break;

            case WeatherState.Rain:
                gpuParticles.SetPrecipitationType(GpuParticles.PrecipitationType.Rain);
                gpuParticles.SetVisible(true);
                gpuParticles.StopEmission();
                if (forceClear || switchingBetweenRainAndSnow)
                    gpuParticles.ClearParticlesNow();
                gpuParticles.StartEmission();
                break;

            case WeatherState.Snow:
                gpuParticles.SetPrecipitationType(GpuParticles.PrecipitationType.Snow);
                gpuParticles.SetVisible(true);
                gpuParticles.StopEmission();
                if (forceClear || switchingBetweenRainAndSnow)
                    gpuParticles.ClearParticlesNow();
                gpuParticles.StartEmission();
                break;
        }
    }
}
