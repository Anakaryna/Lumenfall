using UnityEngine;

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

    [Header("Debug Keys")]
    public bool enableDebugKeys = true;
    public KeyCode sunnyKey = KeyCode.Alpha1;
    public KeyCode cloudyKey = KeyCode.Alpha2;
    public KeyCode rainKey = KeyCode.Alpha3;
    public KeyCode snowKey = KeyCode.Alpha4;

    void Start()
    {
        ApplyState(currentState, true);
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