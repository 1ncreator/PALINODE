namespace Palinode.Cutscene
{
    /// <summary>Implemented by layer add-ons (texts, lights, particles) that must follow the layer's effective alpha.</summary>
    public interface IStageAlpha
    {
        void SetStageAlpha(float alpha);
    }
}
