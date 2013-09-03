﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bomberman.Game.Elements.Cells;
using Bomberman.Game.Elements.Items;
using Bomberman.Game.Elements.Players.Input;
using Bomberman.Game.Elements.Fields;
using BomberEngine.Debugging;
using BomberEngine.Util;
using BomberEngine.Consoles;
using Lidgren.Network;
using BomberEngine.Game;
using BomberEngine.Core;
using Bomberman.Content;

namespace Bomberman.Game.Elements.Players
{
    public class Player : MovableCell
    {
        private static readonly PlayerAction[] ACTIONS = 
        {
            PlayerAction.Up,
            PlayerAction.Left,
            PlayerAction.Down,
            PlayerAction.Right,
            PlayerAction.Bomb,
            PlayerAction.Special
        };

        private int m_index;
        private int m_triggerBombsCount;

        private bool m_alive;

        private bool m_ready;

        public bool needsFieldState;
        public bool needsRoundResults;
        
        private PlayerInput m_input;
        private BombList m_bombs;
        private PowerupList m_powerups;
        private DiseaseList m_diseases;

        private Bomb m_bombInHands;

        /* Kicked/Punched bombs */
        private List<Bomb> m_thrownBombs;

        private NetConnection m_connection;

        private int m_lastAckPacketId;  // last acknowledged packet id
        private int m_lastSentPacketId; // last sent packet id

        private float m_errDx;
        private float m_errDy;

        private int m_winsCount;
        private int m_suicidesCount;

        private PlayerAnimations m_animations;
        private AnimationInstance m_currentAnimation;

        private bool m_punchingBomb; // true if player is in the middle of punching a bomb (animation is playing)
        private bool m_pickingBomb; // true if player is in the middle of picking a bomb (animation is playing)

        public Player(int index)
            : base(FieldCellType.Player, 0, 0)
        {
            m_index = index;
            m_alive = true;

            InitPowerups();
            InitBombs();
            InitDiseases();
            InitPlayer();

            m_thrownBombs = new List<Bomb>();

            ResetNetworkState();
        }

        public override void Reset()
        {
            base.Reset();

            m_input.Reset();

            SetCell(0, 0);

            m_alive = true;

            ResetPowerups();
            ResetBombs();
            ResetDiseases();
            ResetPlayer();
            ResetAnimation();
            
            m_thrownBombs.Clear();

            ResetNetworkState();
        }

        public void ResetNetworkState()
        {
            m_ready = false;
            needsFieldState = true;
            needsRoundResults = true;
        }

        public override void Destroy()
        {
            base.Destroy();
            m_connection = null;
        }

        //////////////////////////////////////////////////////////////////////////////

        #region IUpdatable

        public override void Update(float delta)
        {
            base.Update(delta);

            if (IsAlive)
            {
                UpdateInput(delta);

                if (m_bombInHands != null)
                {
                    m_bombInHands.Update(delta);
                }

                m_diseases.Update(delta);
            }

            UpdateAnimation(delta);

            for (int bombIndex = 0; bombIndex < m_thrownBombs.Count; ++bombIndex)
            {
                m_thrownBombs[bombIndex].Update(delta);
            }
        }

        public override void UpdateDumb(float delta)
        {
            base.UpdateDumb(delta);
            if (IsAlive)
            {
                UpdateInput(delta);
            }
        }


        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Movable

        /* Player needs to overcome obstacles */
        protected override float GetMoveTargetDx(float delta, bool blocked)
        {
            float xOffset;

            if (blocked)
            {
                float cOffset = CenterOffX;
                if (Math.Abs(cOffset) < 0.01f) // if target offset is really small (more like calculation error) - don't try to come around obstacle
                {
                    return -cOffset;
                }

                int dcx = Math.Sign(cOffset);
                int dcy = Math.Sign(moveKy);

                Debug.Assert(dcx != 0);
                Debug.Assert(dcy != 0);

                if (HasNearObstacle(0, dcy)) // can't go ahead?
                {
                    if (!HasNearObstacle(dcx, dcy)) // it's ok to take the shorter path
                    {
                        xOffset = Util.Cx2Px(cx + dcx) - px;
                    }
                    else if (!HasNearObstacle(-dcx, dcy)) // it's ok to take the longer path
                    {
                        xOffset = Util.Cx2Px(cx - dcx) - px;
                    }
                    else // no way to go
                    {
                        return 0.0f;
                    }
                }
                else
                {
                    xOffset = Util.TargetPxOffset(px);
                }
            }
            else
            {
                xOffset = Util.TargetPxOffset(px);
            }
            return xOffset < 0 ? Math.Max(xOffset, -delta * GetSpeed()) : Math.Min(xOffset, delta * GetSpeed());
        }

        /* Player needs to overcome obstacles */
        protected override float GetMoveTargetDy(float delta, bool blocked)
        {
            float yOffset;

            if (blocked)
            {
                float cOffset = CenterOffY;
                if (Math.Abs(cOffset) < 0.01f) // if target offset is really small (more like calculation error) - don't try to come around obstacle
                {
                    return -cOffset;
                }

                int dcx = Math.Sign(moveKx);
                int dcy = Math.Sign(cOffset);

                Debug.Assert(dcx != 0);
                Debug.Assert(dcy != 0);

                if (HasNearObstacle(dcx, 0)) // can't go ahead?
                {
                    if (!HasNearObstacle(dcx, dcy)) // it's ok to take the shorter path
                    {
                        yOffset = Util.Cy2Py(cy + dcy) - py;
                    }
                    else if (!HasNearObstacle(dcx, -dcy)) // it's ok to take the longer path
                    {
                        yOffset = Util.Cy2Py(cy - dcy) - py;
                    }
                    else // no way to go
                    {
                        return 0.0f;
                    }
                }
                else
                {
                    yOffset = Util.TargetPyOffset(py);
                }
            }
            else
            {
                yOffset = Util.TargetPyOffset(py);
            }

            return yOffset < 0 ? Math.Max(yOffset, -delta * GetSpeed()) : Math.Min(yOffset, delta * GetSpeed());
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Input

        private void UpdateInput(float delta)
        {
            if (m_input.IsActive)
            {
                m_input.Update(delta);
            }

            if (!GetField().IsGameDumbMuliplayerClient || IsNetworkPlayer)
            {
                for (int i = 0; i < ACTIONS.Length; ++i)
                {
                    PlayerAction action = ACTIONS[i];
                    if (m_input.IsActionJustPressed(action))
                    {
                        OnActionPressed(m_input, action);
                    }
                }

                for (int i = 0; i < ACTIONS.Length; ++i)
                {
                    PlayerAction action = ACTIONS[i];
                    if (m_input.IsActionJustReleased(action))
                    {
                        OnActionReleased(m_input, action);
                    }
                }
            }
        }

        public void OnActionPressed(PlayerInput playerInput, PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Up:
                {
                    StartMovingToDirection(Direction.UP);
                    break;
                }

                case PlayerAction.Down:
                {
                    StartMovingToDirection(Direction.DOWN);
                    break;
                }

                case PlayerAction.Left:
                {
                    StartMovingToDirection(Direction.LEFT);
                    break;
                }

                case PlayerAction.Right:
                {
                    StartMovingToDirection(Direction.RIGHT);
                    break;
                }

                case PlayerAction.Bomb:
                {
                    TryAction();
                    break;
                }

                case PlayerAction.Special:
                {
                    TrySpecialAction();
                    break;
                }
            }
        }

        public void OnActionReleased(PlayerInput playerInput, PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Left:
                case PlayerAction.Right:
                case PlayerAction.Up:
                case PlayerAction.Down:
                    StopMoving();
                    break;
            }

            if (playerInput.GetPressedActionCount() > 0)
            {
                if (playerInput.IsActionPressed(PlayerAction.Up))
                {
                    StartMovingToDirection(Direction.UP);
                }
                else if (playerInput.IsActionPressed(PlayerAction.Down))
                {
                    StartMovingToDirection(Direction.DOWN);
                }

                if (playerInput.IsActionPressed(PlayerAction.Left))
                {
                    StartMovingToDirection(Direction.LEFT);
                }
                else if (playerInput.IsActionPressed(PlayerAction.Right))
                {
                    StartMovingToDirection(Direction.RIGHT);
                }
            }

            if (action == PlayerAction.Bomb)
            {
                TryThrowBomb();
            }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////
        
        #region Movement

        private void StartMovingToDirection(Direction dir)
        {
            SetMoveDirection(dir);
            UpdateAnimation();
        }

        public override void StopMoving()
        {
            base.StopMoving();
            UpdateAnimation();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Collisions

        public override bool Collides(FieldCell other)
        {
            if (other.IsPlayer())
            {
                return CheckBounds2BoundsCollision(other);
            }

            return CheckBounds2CellCollision(other);
        }

        public override bool HandleWallCollision()
        {
            return false;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Powerups

        private void InitPowerups()
        {
            CVar[] initials = CVars.powerupsInitials;
            CVar[] max = CVars.powerupsMax;

            int totalCount = initials.Length;
            m_powerups = new PowerupList(this, totalCount);
            for (int powerupIndex = 0; powerupIndex < totalCount; ++powerupIndex)
            {
                int initialCount = initials[powerupIndex].intValue;
                int maxCount = max[powerupIndex].intValue;
                m_powerups.Init(powerupIndex, initialCount, maxCount);
            }
        }

        private void ResetPowerups()
        {
            CVar[] initials = CVars.powerupsInitials;
            CVar[] max = CVars.powerupsMax;

            int totalCount = initials.Length;
            for (int powerupIndex = 0; powerupIndex < totalCount; ++powerupIndex)
            {
                int initialCount = initials[powerupIndex].intValue;
                int maxCount = max[powerupIndex].intValue;
                m_powerups.Init(powerupIndex, initialCount, maxCount);
            }
        }

        public bool TryAddPowerup(int powerupIndex)
        {
            bool added = m_powerups.Inc(powerupIndex);
            if (!added)
            {
                return false;
            }

            switch (powerupIndex)
            {
                case Powerups.Bomb:
                {
                    m_bombs.IncMaxActiveCount();

                    if (HasTrigger())
                    {
                        ++m_triggerBombsCount;
                    }

                    break;
                }

                case Powerups.Speed:
                {
                    SetSpeed(CalcPlayerSpeed());
                    break;
                }

                case Powerups.Flame:
                case Powerups.GoldFlame:
                {   
                    break;
                }

                // Trigger will drop Jelly and Boxing Glove
                case Powerups.Trigger:
                {
                    m_triggerBombsCount = m_bombs.GetMaxActiveCount();

                    TryGivePowerupBack(Powerups.Jelly);
                    TryGivePowerupBack(Powerups.Punch);
                    break;
                }

                // Jelly will drop Trigger
                // Boxing Glove will drop Trigger
                case Powerups.Jelly:
                case Powerups.Punch:
                {
                    TryGivePowerupBack(Powerups.Trigger);
                    break;
                }

                // Blue Hand will drop Spooge
                case Powerups.Grab:
                {
                    TryGivePowerupBack(Powerups.Spooger);
                    break;
                }

                // Spooge will drop Blue Hand
                case Powerups.Spooger:
                {
                    TryGivePowerupBack(Powerups.Grab);
                    break;
                }

                case Powerups.Disease:
                {
                    InfectRandom(1);
                    break;
                }

                case Powerups.Ebola:
                {
                    InfectRandom(3);
                    break;
                }
            }

            return true;
        }

        public void OnInfected(Diseases desease)
        {
            if (desease == Diseases.POOPS)
            {
                TryPoops();
            }
        }

        public void OnCured(Diseases desease)
        {
        }

        public void InfectRandom(int count)
        {
            m_diseases.InfectRandom(count);
        }

        public bool HasKick()
        {
            return HasPowerup(Powerups.Kick);
        }

        public bool HasPunch()
        {
            return HasPowerup(Powerups.Punch);
        }

        public bool HasSpooger()
        {
            return HasPowerup(Powerups.Spooger);
        }

        public bool HasTrigger()
        {
            return HasPowerup(Powerups.Trigger);
        }

        public bool HasGrab()
        {
            return HasPowerup(Powerups.Grab);
        }

        private bool HasPowerup(int powerupIndex)
        {
            return m_powerups.HasPowerup(powerupIndex);
        }

        private void TryGivePowerupBack(int powerupIndex)
        {
            if (m_powerups.HasPowerup(powerupIndex))
            {
                switch (powerupIndex)
                {
                    case Powerups.Trigger:
                        m_triggerBombsCount = 0;
                        break;
                }

                GivePowerupBack(powerupIndex);
            }
        }

        private void GivePowerupBack(int powerupIndex)
        {
            Debug.Assert(m_powerups.GetCount(powerupIndex) == 1);
            m_powerups.SetCount(powerupIndex, 0);

            GetField().PlacePowerup(powerupIndex);
        }

        private bool IsInfectedPoops()
        {
            return IsInfected(Diseases.POOPS);
        }

        private bool IsInfectedShortFuze()
        {
            return IsInfected(Diseases.SHORTFUZE);
        }

        private bool IsInfectedShortFlame()
        {
            return IsInfected(Diseases.SHORTFLAME);
        }

        private bool IsInfectedConstipation()
        {
            return IsInfected(Diseases.CONSTIPATION);
        }

        private bool IsInfected(Diseases disease)
        {
            return m_diseases.IsInfected(disease);
        }

        private int CalcPlayerSpeed()
        {
            int speedBase = CVars.cg_playerSpeed.intValue;
            int speedAdd = CVars.cg_playerSpeedAdd.intValue;
            return speedBase + speedAdd * m_powerups.GetCount(Powerups.Speed);
        }

        private int CalcBombsCount()
        {
            return m_powerups.GetCount(Powerups.Bomb);
        }

        private float CalcBombTimeout()
        {
            return CVars.cg_fuzeTimeNormal.intValue * 0.001f;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Bombs

        private void InitBombs()
        {
            int maxBombs = CVars.cg_maxBomb.intValue;
            m_bombs = new BombList(this, maxBombs);
            m_bombs.SetMaxActiveCount(CalcBombsCount());
        }

        private void ResetBombs()
        {
            m_bombs.Reset();
            m_bombs.SetMaxActiveCount(CalcBombsCount());
        }

        private bool TryKick(Bomb bomb)
        {
            FieldCellSlot blockingSlot = bomb.GetNearSlot(direction);
            if (blockingSlot != null && !blockingSlot.ContainsObstacle())
            {
                KickBomb(bomb);
                return true;
            }

            return false;
        }

        private void KickBomb(Bomb bomb)
        {
            switch (direction)
            {
                case Direction.RIGHT:
                case Direction.LEFT:
                    ForcePos(CellCenterPx(), py);
                    break;

                case Direction.UP:
                case Direction.DOWN:
                    ForcePos(px, CellCenterPy());
                    break;
            }

            bomb.Kick(direction);
        }

        public void OnBombBlown(Bomb bomb)
        {
            TrySchedulePoops();
        }

        private Bomb GetNextBomb()
        {
            if (IsInfectedConstipation())
            {
                return null;
            }

            return m_bombs.GetNextBomb();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Kicked/Punched bombs

        private void AddThrownBomb(Bomb bomb)
        {
            Debug.AssertNotContains(m_thrownBombs, bomb);
            m_thrownBombs.Add(bomb);
        }

        private void RemoveThrownBomb(Bomb bomb)
        {
            bool removed = m_thrownBombs.Remove(bomb);
            Debug.Assert(removed);
        }

        public void OnBombLanded(Bomb bomb)
        {
            RemoveThrownBomb(bomb);
            bomb.SetCell();
            GetField().SetBomb(bomb);
        }

        public Bomb bombInHands
        {
            get { return m_bombInHands; }
        }

        public List<Bomb> thrownBombs
        {
            get { return m_thrownBombs; }
        }

        public bool IsHoldingBomb()
        {
            return m_bombInHands != null;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Disease

        private void InitDiseases()
        {
            m_diseases = new DiseaseList(this);
        }

        private void ResetDiseases()
        {
            m_diseases.Reset();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Player init

        private void InitPlayer()
        {
            SetSpeed(CalcPlayerSpeed());
        }

        private void ResetPlayer()
        {
            InitPlayer();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Player input

        public void SetPlayerInput(PlayerInput input)
        {
            this.m_input = input;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Kill

        // should be only called from PlayeList
        internal void Kill()
        {
            Debug.Assert(m_alive);
            m_alive = false;
            
            StopMoving();
            if (m_bombInHands != null)
            {
                m_bombInHands.isActive = false;
                m_bombInHands = null;
            }

            m_diseases.CureAll();

            UpdateAnimation();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Inheritance

        protected override void OnCellChanged(int oldCx, int oldCy)
        {
            TryPoops();
            base.OnCellChanged(oldCx, oldCy);
        }

        public override Player AsPlayer()
        {
            return this;
        }

        public override bool IsPlayer()
        {
            return true;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Actions

        public void TryAction()
        {
            bool bombSet = TryBomb();
            if (!bombSet)
            {
                if (HasSpooger())
                {
                    TrySpooger();
                }
                else if (HasGrab())
                {
                    TryGrab();
                }
            }
        }

        private void TrySpecialAction()
        {
            if (HasKick())
            {
                TryStopBomb();
            }
            if (HasTrigger())
            {
                TryTriggerBomb();
            }
            if (HasPunch())
            {
                TryPunchBomb();
            }
        }

        private void TryStopBomb()
        {
            Bomb kickedBomb = m_bombs.GetFirstKickedBomb();
            if (kickedBomb != null)
            {
                kickedBomb.TryStopKicked();
            }
        }

        private bool TrySpooger()
        {
            Bomb underlyingBomb = GetField().GetSlot(cx, cy).GetBomb();
            if (underlyingBomb == null)
            {
                return false; // you can use spooger only when standing on the bomb
            }

            if (underlyingBomb.player != this)
            {
                return false; // you only can use spooger when standing at your own bomb
            }

            switch (direction)
            {
                case Direction.UP:
                    return TrySpooger(0, -1);

                case Direction.DOWN:
                    return TrySpooger(0, 1);

                case Direction.LEFT:
                    return TrySpooger(-1, 0);

                case Direction.RIGHT:
                    return TrySpooger(1, 0);

                default:
                    Debug.Assert(false, "Unknown direction: " + direction);
                    break;
            }

            return false;
        }

        private bool TrySpooger(int dcx, int dcy)
        {
            int uCx = cx + dcx;
            int uCy = cy + dcy;

            int numBombs = 0;
            Field field = GetField();

            while (field.ContainsNoObstacle(uCx, uCy))
            {
                Bomb nextBomb = GetNextBomb();
                if (nextBomb == null)
                {
                    break; // no bombs to apply
                }

                nextBomb.SetCell(uCx, uCy);
                field.SetBomb(nextBomb);

                uCx += dcx;
                uCy += dcy;
                ++numBombs;
            }

            return numBombs > 0;
        }

        private bool TryTriggerBomb()
        {
            Bomb triggerBomb = m_bombs.GetFirstTriggerBomb();
            if (triggerBomb != null)
            {
                triggerBomb.Blow();
                return true;
            }

            return false;
        }

        private bool TryBomb()
        {   
            if (GetField().ContainsNoObstacle(cx, cy))
            {
                Bomb bomb = GetNextBomb();
                if (bomb != null)
                {
                    GetField().SetBomb(bomb);
                    if (HasTrigger() && m_triggerBombsCount > 0)
                    {
                        --m_triggerBombsCount;
                    }
                    return true;
                }
            }
            return false;
        }

        private bool TryGrab()
        {
            Bomb underlyingBomb = GetField().GetBomb(cx, cy);
            if (underlyingBomb != null)
            {
                underlyingBomb.Grab();
                m_bombInHands = underlyingBomb;

                IsPickingUpBomb = true;
                return true;
            }
            return false;
        }

        private bool TryThrowBomb()
        {
            if (IsHoldingBomb())
            {
                AddThrownBomb(m_bombInHands);
                m_bombInHands.Throw();
                m_bombInHands = null;

                IsPickingUpBomb = false;
                return true;
            }
            return false;
        }

        private bool TryThrowAllBombs()
        {
            int bombsCount = 0;

            Bomb nextBomb;
            while ((nextBomb = GetNextBomb()) != null)
            {
                AddThrownBomb(nextBomb);
                nextBomb.Throw();
            }

            return bombsCount > 0;
        }
        
        private bool TryPunchBomb()
        {
            IsPunchingBomb = true;

            FieldCellSlot slot = GetNearSlot(direction);
            Bomb bomb = slot != null ? slot.GetBomb() : null;
            if (bomb != null)
            {   
                AddThrownBomb(bomb);
                bomb.Punch();
                return true;
            }

            return false;
        }

        private bool TryPoops()
        {
            if (IsInfectedPoops())
            {
                if (HasGrab())
                {
                    return TryThrowAllBombs();
                }

                return TryBomb();
            }

            return false;
        }

        private bool TrySchedulePoops()
        {
            if (IsInfectedPoops())
            {
                ScheduleTimerOnce(TrySchedulePoopsCallback);
                return true;
            }

            return false;
        }

        private void TrySchedulePoopsCallback(Timer timer)
        {
            TryPoops();
        }

        public bool TryInfect(int diseaseIndex)
        {
            return m_diseases.TryInfect(diseaseIndex);
        }

        public bool IsInfected()
        {
            return m_diseases.activeCount > 0;
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Round

        public int winsCount
        {
            get { return m_winsCount; }
            set { m_winsCount = value; }
        }

        public int suicidesCount
        {
            get { return m_suicidesCount; }
            set { m_suicidesCount = value; }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Animations

        private void InitAnimation()
        {
            m_currentAnimation = new AnimationInstance();
            UpdateAnimation();
        }

        public override void UpdateAnimation(float delta)
        {
            if (m_currentAnimation != null)
            {
                m_currentAnimation.Update(delta);
            }
        }

        private void UpdateAnimation()
        {
            PlayerAnimations.Id id;
            PlayerAnimations.Id currentId = (PlayerAnimations.Id)m_currentAnimation.id;
            AnimationInstance.Mode mode = AnimationInstance.Mode.Looped;

            if (IsAlive)
            {
                if (IsPunchingBomb)
                {
                    if (currentId == PlayerAnimations.Id.PunchBomb)
                    {
                        return; // don't play animation again
                    }

                    id = PlayerAnimations.Id.PunchBomb;
                    mode = AnimationInstance.Mode.Normal;
                }
                else if (IsPickingUpBomb)
                {
                    id = PlayerAnimations.Id.PickupBomb;
                    mode = AnimationInstance.Mode.Normal;
                }
                else if (IsHoldingBomb())
                {
                    id = IsMoving() ? PlayerAnimations.Id.WalkBomb : PlayerAnimations.Id.StandBomb;
                    mode = AnimationInstance.Mode.Normal;
                }
                else if (IsMoving())
                {
                    id = PlayerAnimations.Id.Walk;
                    Animation newAnimation = m_animations.Find(id, direction);
                    if (m_currentAnimation.Animation == newAnimation)
                    {
                        return;
                    }
                }
                else
                {
                    id = PlayerAnimations.Id.Stand;
                }
            }
            else
            {
                id = PlayerAnimations.Id.Die;
                mode = AnimationInstance.Mode.Normal;
            }

            Animation animation = m_animations.Find(id, direction);
            m_currentAnimation.Init(animation, mode);
            m_currentAnimation.id = (int)id;
            m_currentAnimation.animationDelegate = AnimationFinishedCallback;
        }

        private void ScheduleAnimationUpdate()
        {
            ScheduleTimerOnce(UpdateAnimationCallback);
        }

        private void UpdateAnimationCallback(Timer timer)
        {
            UpdateAnimation();
        }

        private void ResetAnimation()
        {
            m_pickingBomb = false;
            m_punchingBomb = false;

            UpdateAnimation();
        }

        private void AnimationFinishedCallback(AnimationInstance animation)
        {
            PlayerAnimations.Id id = (PlayerAnimations.Id)animation.id;
            switch (id)
            {
                case PlayerAnimations.Id.Die:
                {
                    Debug.Assert(!IsAlive);
                    RemoveFromField();
                    break;
                }

                case PlayerAnimations.Id.PunchBomb:
                {
                    IsPunchingBomb = false;
                    break;
                }

                case PlayerAnimations.Id.PickupBomb:
                {
                    IsPickingUpBomb = false;
                    break;
                }
            }
        }

        private bool IsPickingUpBomb
        {
            get { return m_pickingBomb; }
            set
            {   
                m_pickingBomb = value;
                ScheduleAnimationUpdate();
            }
        }

        private bool IsPunchingBomb
        {
            get { return m_punchingBomb; }
            set
            {   
                m_punchingBomb = value;
                ScheduleAnimationUpdate();
            }
        }
        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Network

        /* Sets player state received from the server as a part of game packet */
        internal void UpdateFromNetwork(float newPx, float newPy, bool moving, Direction newDir, float newSpeed)
        {
            m_errDx = px - newPx;
            m_errDy = py - newPy;

            if (px != newPx || py != newPy)
            {   
                SetPos(newPx, newPy);
            }

            if (!moving)
            {
                if (IsMoving())
                {
                    StopMoving();
                }
            }
            else
            {
                SetSpeed(newSpeed);
                if (newDir != direction)
                {
                    StartMovingToDirection(newDir);
                }
            }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Properties

        public float GetBombTimeout()
        {
            CVar var = IsInfectedShortFuze() ? CVars.cg_fuzeTimeShort : CVars.cg_fuzeTimeNormal;
            return var.intValue * 0.001f;
        }

        public int GetBombRadius()
        {
            if (IsInfectedShortFlame())
            {
                return CVars.cg_bombShortFlame.intValue;
            }
            if (m_powerups.HasPowerup(Powerups.GoldFlame))
            {
                return int.MaxValue;
            }
            return m_powerups.GetCount(Powerups.Flame);
        }

        public bool IsJelly()
        {
            return HasPowerup(Powerups.Jelly);
        }

        public bool IsTrigger()
        {
            return HasTrigger() && m_triggerBombsCount > 0;
        }

        public PowerupList powerups
        {
            get { return m_powerups; }
        }

        public BombList bombs
        {
            get { return m_bombs; }
        }

        public PlayerInput input
        {
            get { return m_input; }
        }

        public NetConnection connection
        {
            get { return m_connection; }
            set { m_connection = value; }
        }

        public DiseaseList diseases
        {
            get { return m_diseases; }
        }

        public int lastAckPacketId
        {
            get { return m_lastAckPacketId; }
            set { m_lastAckPacketId = value; }
        }

        public int lastSentPacketId
        {
            get { return m_lastSentPacketId; }
            set { m_lastSentPacketId = value; }
        }

        public int networkPackageDiff
        {
            get { return m_lastSentPacketId - m_lastAckPacketId; }
        }

        public PlayerAnimations animations
        {
            get { return m_animations; }
            set 
            { 
                m_animations = value;
                if (m_animations != null)
                {
                    InitAnimation();
                }
            }
        }

        public BombAnimations bombAnimations
        {
            set
            {
                for (int i = 0; i < bombs.array.Length; ++i)
                {
                    bombs.array[i].animations = value;
                }
            }
        }

        public AnimationInstance currentAnimation
        {
            get { return m_currentAnimation; }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Getters/Setters

        public bool IsAlive
        {
            get { return m_alive; }
            set { m_alive = value; }
        }

        public bool IsNetworkPlayer
        {
            get { return m_input is PlayerNetworkInput; }
        }

        public int GetIndex()
        {
            return m_index;
        }

        public bool IsReady
        {
            get { return m_ready; }
            set { m_ready = value; }
        }

        public float errDx
        {
            get { return m_errDx; }
        }

        public float errDy
        {
            get { return m_errDy; }
        }

        #endregion
    }
}
