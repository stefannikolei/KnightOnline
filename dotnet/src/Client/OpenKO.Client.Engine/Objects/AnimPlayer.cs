using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Pure port of the CN3Chr __FrmCtrl frame machine (lower body; the
/// upper-body split follows with the game port in stage 7):
/// TickAnimationFrame's looping, once-and-freeze, freeze-time, loop-delay
/// (iBlendFlags &amp; 1) and motion-blend timing — verbatim.
/// </summary>
public sealed class AnimPlayer
{
    public N3AnimData? Data { get; private set; }

    public float FrmCur { get; private set; }

    public float FrmPrev { get; private set; }

    public float BlendFrm { get; private set; }

    public float BlendTime { get; private set; }

    public float BlendTimeCur { get; private set; }

    public float FreezeTime { get; private set; }

    public int AniLoop { get; private set; }

    public bool OnceAndFreeze { get; private set; }

    public bool ProcessingDelayNow { get; private set; }

    /// <summary>Blend factor for TickJoints (0 when not blending).</summary>
    public float BlendFactor => BlendTime > 0f ? BlendTimeCur / BlendTime : 0f;

    /// <summary>
    /// CN3Chr::AniCurSet essentials: switches the clip, blending from the
    /// current frame over the clip's blend time when one was playing.
    /// </summary>
    public void SetAnim(N3AnimData data, bool onceAndFreeze = false, float freezeTime = 0f)
    {
        float blendFrom = FrmCur;
        bool wasPlaying = Data != null;

        Data = data;
        OnceAndFreeze = onceAndFreeze;
        FreezeTime = freezeTime;
        AniLoop = 0;
        ProcessingDelayNow = false;
        FrmCur = data.FrmStart;
        FrmPrev = data.FrmStart;

        if (wasPlaying && data.TimeBlend > 0f)
        {
            BlendFrm = blendFrom;
            BlendTime = data.TimeBlend;
            BlendTimeCur = 0f;
        }
        else
        {
            BlendTime = 0f;
            BlendTimeCur = 0f;
        }
    }

    /// <summary>CN3Chr::TickAnimationFrame (lower body path).</summary>
    public void Tick(float secPerFrame, float aniSpeedDelta = 1f)
    {
        if (Data == null)
            return;

        FrmPrev = FrmCur;

        // Loop delay: after a loop finished, blend from the end frame back
        // to the start before playing again (iBlendFlags & 1).
        if ((Data.BlendFlags & 1) != 0 && AniLoop > 0 && ProcessingDelayNow && BlendTime == 0f)
        {
            BlendTime = Data.TimeBlend;
            BlendTimeCur = 0f;
            BlendFrm = Data.FrmEnd;
            FrmCur = Data.FrmStart;
            AniLoop = 0;
        }

        float delta = secPerFrame * aniSpeedDelta;

        if (BlendTime != 0f)
        {
            BlendTimeCur += delta;
            if (BlendTimeCur > BlendTime)
            {
                BlendTime = 0f;
                BlendTimeCur = 0f;
                ProcessingDelayNow = false;
            }

            return;
        }

        FrmCur += Data.FrmPerSec * delta;
        if (FrmCur < Data.FrmStart)
            FrmCur = Data.FrmStart;

        if (FrmCur > Data.FrmEnd)
        {
            if (FreezeTime > 0f)
            {
                FrmCur = Data.FrmEnd;
                FreezeTime -= delta;
                if (FreezeTime < 0f)
                {
                    FreezeTime = 0f;
                    AniLoop++;
                }
            }
            else
            {
                FreezeTime = 0f;
                AniLoop++;

                if (OnceAndFreeze)
                {
                    FrmCur = Data.FrmEnd;
                }
                else if ((Data.BlendFlags & 1) != 0)
                {
                    FrmCur = Data.FrmEnd;
                    ProcessingDelayNow = true;
                }
                else
                {
                    float frmDiff = Data.FrmEnd - Data.FrmStart;
                    if (frmDiff > 0f)
                        FrmCur -= frmDiff;
                    else
                        FrmCur = Data.FrmStart;
                }
            }
        }
    }
}
