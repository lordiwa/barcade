namespace Barcade.Presentation
{
    /// <summary>
    /// Pure camera placement output (GDD §10.4). Position is world-space;
    /// <see cref="PitchDeg"/>/<see cref="YawDeg"/> are Euler angles around
    /// world X/Y (no stage rig rolls the camera). Orthographic rigs set
    /// <see cref="OrthoSize"/> and leave <see cref="FieldOfViewDeg"/> at 0;
    /// perspective rigs do the reverse.
    /// </summary>
    public readonly struct CameraPlacement
    {
        public readonly float PosX, PosY, PosZ;
        public readonly float PitchDeg, YawDeg;
        public readonly bool Orthographic;
        public readonly float OrthoSize;
        public readonly float FieldOfViewDeg;

        public CameraPlacement(
            float posX, float posY, float posZ,
            float pitchDeg, float yawDeg,
            bool orthographic, float orthoSize, float fieldOfViewDeg)
        {
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            PitchDeg = pitchDeg;
            YawDeg = yawDeg;
            Orthographic = orthographic;
            OrthoSize = orthoSize;
            FieldOfViewDeg = fieldOfViewDeg;
        }
    }
}
