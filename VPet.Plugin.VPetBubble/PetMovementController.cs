using System;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet.Plugin.VPetBubble
{
    internal enum PetMoveDirection
    {
        Left,
        Right
    }

    internal class PetMovementController
    {
        private static readonly TimeSpan ContinueCheckInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan StopLeadTime = TimeSpan.FromSeconds(5);

        private readonly Main main;
        private readonly IController controller;
        private readonly Func<IGameSave.ModeType> getMood;
        private readonly Random random = new();
        private readonly Dispatcher ui;
        private readonly DispatcherTimer moveTimer;
        private readonly DispatcherTimer continueTimer;
        private readonly DispatcherTimer stopTimer;

        private PetMoveDirection direction;
        private PetMoveDirection lastDirection = PetMoveDirection.Left;
        private double continueProbability;
        private int token;
        private bool stopQueued;

        public PetMovementController(IMainWindow mainWindow)
        {
            main = mainWindow.Main;
            controller = main.Core.Controller;
            getMood = () => main.Core.Save.Mode;
            ui = main.Dispatcher;

            moveTimer = new DispatcherTimer(DispatcherPriority.Normal, ui);
            moveTimer.Interval = TimeSpan.FromMilliseconds(125);
            moveTimer.Tick += MoveTimer_Tick;

            continueTimer = new DispatcherTimer(DispatcherPriority.Normal, ui);
            continueTimer.Interval = ContinueCheckInterval;
            continueTimer.Tick += ContinueTimer_Tick;

            stopTimer = new DispatcherTimer(DispatcherPriority.Normal, ui);
            stopTimer.Interval = StopLeadTime;
            stopTimer.Tick += StopTimer_Tick;
        }

        public bool IsMoving { get; private set; }

        public void StartRandomHorizontalMove(double continueProbability)
        {
            if (IsMoving)
            {
                return;
            }

            ui.BeginInvoke(new Action(() =>
            {
                if (IsMoving)
                {
                    return;
                }

                this.continueProbability = Math.Clamp(continueProbability, 0, 1);
                if (!TryChooseAvailableDirection(out direction))
                {
                    return;
                }
                lastDirection = direction;
                IsMoving = true;
                stopQueued = false;

                var myToken = ++token;
                main.Display(GraphType.Default, AnimatType.Single, () =>
                {
                    if (myToken != token || !IsMoving)
                    {
                        return;
                    }

                    StartMoveTimer();
                    continueTimer.Start();
                    PlayStartThenLoop(myToken);
                });
            }), DispatcherPriority.ApplicationIdle);
        }

        public void Stop()
        {
            ui.BeginInvoke(new Action(() =>
            {
                if (!IsMoving)
                {
                    return;
                }

                StopWithEndAnimation();
            }), DispatcherPriority.ApplicationIdle);
        }

        private void ContinueTimer_Tick(object sender, EventArgs e)
        {
            if (!IsMoving || stopQueued)
            {
                return;
            }

            if (random.NextDouble() <= continueProbability)
            {
                return;
            }

            stopQueued = true;
            stopTimer.Start();
        }

        private void StopTimer_Tick(object sender, EventArgs e)
        {
            stopTimer.Stop();
            if (IsMoving)
            {
                StopWithEndAnimation();
            }
        }

        private void PlayStartThenLoop(int myToken)
        {
            if (!IsMoving)
            {
                return;
            }

            var anim = GetAnimByDirection(getMood(), direction);
            main.Display(anim, AnimatType.A_Start, () =>
            {
                if (myToken != token || !IsMoving)
                {
                    return;
                }

                LoopB(anim, myToken);
            });
        }

        private void LoopB(string anim, int myToken)
        {
            if (myToken != token || !IsMoving)
            {
                return;
            }

            main.Display(anim, AnimatType.B_Loop, () => LoopB(anim, myToken));
        }

        private void StopWithEndAnimation()
        {
            IsMoving = false;
            stopQueued = false;
            continueTimer.Stop();
            stopTimer.Stop();
            StopMoveTimer();

            var myToken = ++token;
            var anim = GetAnimByDirection(getMood(), lastDirection);
            main.Display(anim, AnimatType.C_End, () =>
            {
                if (myToken != token)
                {
                    return;
                }

                main.DisplayToNomal();
            });
        }

        private void MoveTimer_Tick(object sender, EventArgs e)
        {
            if (!IsMoving)
            {
                return;
            }

            var speed = GetHorizontalSpeedByMood(getMood());
            var deltaX = direction == PetMoveDirection.Left
                ? -Math.Min(speed, Math.Max(0, controller.GetWindowsDistanceLeft() / controller.ZoomRatio))
                : Math.Min(speed, Math.Max(0, controller.GetWindowsDistanceRight() / controller.ZoomRatio));

            if (Math.Abs(deltaX) < 0.1)
            {
                StopWithEndAnimation();
                return;
            }

            controller.MoveWindows(deltaX, 0);
        }

        private bool TryChooseAvailableDirection(out PetMoveDirection availableDirection)
        {
            var canMoveLeft = CanMove(PetMoveDirection.Left);
            var canMoveRight = CanMove(PetMoveDirection.Right);

            if (canMoveLeft && canMoveRight)
            {
                availableDirection = random.Next(2) == 0 ? PetMoveDirection.Left : PetMoveDirection.Right;
                return true;
            }

            if (canMoveLeft)
            {
                availableDirection = PetMoveDirection.Left;
                return true;
            }

            if (canMoveRight)
            {
                availableDirection = PetMoveDirection.Right;
                return true;
            }

            availableDirection = PetMoveDirection.Left;
            return false;
        }

        private bool CanMove(PetMoveDirection direction)
        {
            return direction == PetMoveDirection.Left
                ? controller.GetWindowsDistanceLeft() / controller.ZoomRatio > 1
                : controller.GetWindowsDistanceRight() / controller.ZoomRatio > 1;
        }

        private void StartMoveTimer()
        {
            moveTimer.Start();
        }

        private void StopMoveTimer()
        {
            moveTimer.Stop();
        }

        private static string GetAnimByDirection(IGameSave.ModeType mood, PetMoveDirection direction)
        {
            var lr = direction == PetMoveDirection.Right ? "right" : "left";
            var suffix = mood switch
            {
                IGameSave.ModeType.Happy => ".faster",
                IGameSave.ModeType.Nomal => "",
                IGameSave.ModeType.PoorCondition => ".slow",
                IGameSave.ModeType.Ill => ".slow",
                _ => ""
            };

            return $"walk.{lr}{suffix}";
        }

        private static int GetHorizontalSpeedByMood(IGameSave.ModeType mood)
        {
            return mood switch
            {
                IGameSave.ModeType.Happy => 20,
                IGameSave.ModeType.Nomal => 14,
                IGameSave.ModeType.PoorCondition => 8,
                IGameSave.ModeType.Ill => 8,
                _ => 14
            };
        }
    }
}
