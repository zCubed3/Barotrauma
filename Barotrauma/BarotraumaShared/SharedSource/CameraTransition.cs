﻿using Microsoft.Xna.Framework;
using NLog.Targets;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class CameraTransition
    {
        public bool Running
        {
            get;
            private set;
        }

        public Camera AssignedCamera;
        private readonly Alignment? cameraStartPos;
        private readonly Alignment? cameraEndPos;
        private readonly float? startZoom;
        private readonly float? endZoom;
        public readonly float Duration;
        public readonly bool FadeOut;

        private readonly CoroutineHandle updateCoroutine;

        private Character prevControlled;

        public bool AllowInterrupt = false;
        public bool RemoveControlFromCharacter = true;
        
        public CameraTransition(ISpatialEntity targetEntity, Camera cam, Alignment? cameraStartPos, Alignment? cameraEndPos, bool fadeOut = true, float duration = 10.0f, float? startZoom = null, float? endZoom = null)
        {
            Duration = duration;
            FadeOut = fadeOut;
            this.cameraStartPos = cameraStartPos;
            this.cameraEndPos = cameraEndPos;
            this.startZoom = startZoom;
            this.endZoom = endZoom;
            AssignedCamera = cam;

            if (targetEntity == null) { return; }

            Running = true;
            CoroutineManager.StopCoroutines("CameraTransition");
            updateCoroutine = CoroutineManager.StartCoroutine(Update(targetEntity, cam), "CameraTransition");
        }

        public void Stop()
        {
            CoroutineManager.StopCoroutines(updateCoroutine);
            Running = false;
#if CLIENT
            if (FadeOut) { GUI.ScreenOverlayColor = Color.TransparentBlack; }
            if (prevControlled != null && !prevControlled.Removed)
            {
                Character.Controlled = prevControlled;
            }
#endif
        }

        private IEnumerable<object> Update(ISpatialEntity targetEntity, Camera cam)
        {
            if (targetEntity == null) { yield return CoroutineStatus.Success; }

            prevControlled = Character.Controlled;
            if (RemoveControlFromCharacter)
            {
#if CLIENT
                GameMain.LightManager.LosEnabled = false;
#endif
                Character.Controlled = null;
            }
            cam.TargetPos = Vector2.Zero;

            float startZoom = this.startZoom ?? cam.Zoom;
            float endZoom = this.endZoom ?? 0.5f;
            Vector2 initialCameraPos = cam.Position;
            Vector2? initialTargetPos = targetEntity?.WorldPosition;

            float timer = 0.0f;
            while (timer < Duration)
            {
                if (Screen.Selected != GameMain.GameScreen)
                {
                    yield return new WaitForSeconds(0.1f);
#if CLIENT
                    if (FadeOut) { GUI.ScreenOverlayColor = Color.TransparentBlack; }
#endif
                    Running = false;
                    yield return CoroutineStatus.Success;
                }

                //switched control to some other character during the transition -> remove control again
                if (Character.Controlled != null)
                {
                    prevControlled = Character.Controlled;
                    if (RemoveControlFromCharacter)
                    {
#if CLIENT
                        GameMain.LightManager.LosEnabled = false;
#endif
                        Character.Controlled = null;
                    }
                }

                if (prevControlled != null && prevControlled.Removed)
                {
                    prevControlled = null;
                }
#if CLIENT
                if (AllowInterrupt && PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
                {
                    break;
                }
#endif
                Vector2 minPos = targetEntity.WorldPosition;
                Vector2 maxPos = targetEntity.WorldPosition;
                if (targetEntity is Submarine sub)
                {
                    minPos = new Vector2(sub.WorldPosition.X - sub.Borders.Width / 2, sub.WorldPosition.Y - sub.Borders.Height / 2);
                    maxPos = new Vector2(sub.WorldPosition.X + sub.Borders.Width / 2, sub.WorldPosition.Y + sub.Borders.Height / 2);
                }

                Vector2 startPos = cameraStartPos.HasValue ?
                    new Vector2(
                        MathHelper.Lerp(minPos.X, maxPos.X, (cameraStartPos.Value.ToVector2().X + 1.0f) / 2.0f),
                        MathHelper.Lerp(maxPos.Y, minPos.Y, (cameraStartPos.Value.ToVector2().Y + 1.0f) / 2.0f)) :
                    initialCameraPos;
                if (!cameraStartPos.HasValue && initialTargetPos.HasValue)
                {
                    startPos += targetEntity.WorldPosition - initialTargetPos.Value;
                }
                Vector2 endPos = cameraEndPos.HasValue ?                    
                    new Vector2(
                        MathHelper.Lerp(minPos.X, maxPos.X, (cameraEndPos.Value.ToVector2().X + 1.0f) / 2.0f),
                        MathHelper.Lerp(maxPos.Y, minPos.Y, (cameraEndPos.Value.ToVector2().Y + 1.0f) / 2.0f)) :
                    prevControlled?.WorldPosition ?? targetEntity.WorldPosition;

                Vector2 cameraPos = Vector2.SmoothStep(startPos, endPos, timer / Duration);
                cam.Translate(cameraPos - cam.Position);
                
#if CLIENT
                cam.Zoom = MathHelper.SmoothStep(startZoom, endZoom, timer / Duration);
                if (timer / Duration > 0.9f)
                {
                    if (FadeOut) { GUI.ScreenOverlayColor = Color.Lerp(Color.TransparentBlack, Color.Black, ((timer / Duration) - 0.9f) * 10.0f); }
                }
#endif
                timer += CoroutineManager.UnscaledDeltaTime;

                yield return CoroutineStatus.Running;
            }

            Running = false;

            yield return new WaitForSeconds(0.1f);

#if CLIENT
            GUI.ScreenOverlayColor = Color.TransparentBlack;
            GameMain.LightManager.LosEnabled = true;
#endif

            if (prevControlled != null && !prevControlled.Removed)
            {
                Character.Controlled = prevControlled;
            }

            yield return CoroutineStatus.Success;
        }
    }
}
