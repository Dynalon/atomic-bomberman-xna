﻿using System;
using System.Collections.Generic;
using BomberEngine.Debugging;
using Bomberman.Game.Elements.Cells;
using Bomberman.Game.Elements.Fields;
using Bomberman.Game.Elements.Players;
using Bomberman.Game.Elements.Players.Input;
using Bomberman.Multiplayer;
using Bomberman.Networking;
using Lidgren.Network;
using Bomberman.Game.Elements;
using Bomberman.Game.Screens;

namespace Bomberman.Game.Multiplayer
{
    public struct ClientPacket
    {
        public int id;
        public int lastAckServerPacketId;
        public float timeStamp;
        public int actions;
    }

    public struct ServerPacket
    {
        public int id;
        public int lastAckClientPacketId;
        public float timeStamp;
    }

    public abstract class GameControllerNetwork : GameController
    {
        public GameControllerNetwork(GameSettings settings)
            : base(settings)
        {
        }

        protected override void OnStart()
        {
            base.OnStart();
            StartListeningPeerRequests();
        }

        protected override void OnStop()
        {
            StopListeningPeerRequests();
            StopListeningPeerMessages();

            StopNetworkPeer();

            base.OnStop();
        }

        //////////////////////////////////////////////////////////////////////////////

        #region Protocol

        private const byte CELL_FLAME   = 0;
        private const byte CELL_BRICK   = 1;
        private const byte CELL_POWERUP = 2;

        private static readonly int BITS_FOR_STATIC_CELL = NetUtility.BitsToHoldUInt(2);
        private static readonly int BITS_FOR_POWERUP = NetUtility.BitsToHoldUInt(Powerups.Count - 1);

        protected void WriteFieldState(NetOutgoingMessage response, Player senderPlayer)
        {
            Field field = game.Field;
            Debug.Assert(field != null);

            // powerups
            FieldCellSlot[] slots = field.GetCells().slots;
            for (int i = 0; i < slots.Length; ++i)
            {
                BrickCell brick = slots[i].GetBrick();
                if (brick != null)
                {
                    response.Write((byte)brick.powerup);
                }
            }

            // players
            List<Player> players = game.GetPlayers().list;

            int senderIndex = players.IndexOf(senderPlayer);
            Debug.Assert(senderIndex != -1);
            response.Write((byte)senderIndex);

            response.Write((byte)players.Count);
            for (int i = 0; i < players.Count; ++i)
            {
                Player player = players[i];
                response.Write((byte)player.cx);
                response.Write((byte)player.cy);
            }
        }

        protected void ReadFieldState(NetIncomingMessage response)
        {
            Field field = game.Field;
            Debug.Assert(field != null);

            // powerups
            FieldCellSlot[] slots = field.GetCells().slots;
            for (int i = 0; i < slots.Length; ++i)
            {
                BrickCell brick = slots[i].GetBrick();
                if (brick != null)
                {
                    brick.powerup = response.ReadByte();
                }
            }

            // players
            int senderIndex = response.ReadByte();
            int playersCount = response.ReadByte();
            for (int i = 0; i < playersCount; ++i)
            {
                Player player = new Player(i);
                int cx = response.ReadByte();
                int cy = response.ReadByte();
                player.SetCell(cx, cy);
                if (senderIndex != i)
                {
                    player.SetPlayerInput(new PlayerNetworkInput());
                }

                game.AddPlayer(player);
            }
        }

        /* a client sends data to the server */
        protected void WriteClientPacket(NetOutgoingMessage msg, ref ClientPacket packet)
        {
            msg.Write(packet.id);
            msg.Write(packet.lastAckServerPacketId);
            msg.WriteTime(packet.timeStamp, false);
            msg.Write(packet.actions, (int)PlayerAction.Count);
        }

        /* the server reads data from a client */
        protected ClientPacket ReadClientPacket(NetIncomingMessage msg)
        {
            ClientPacket packet;
            packet.id = msg.ReadInt32();
            packet.lastAckServerPacketId = msg.ReadInt32();
            packet.timeStamp = (float)msg.ReadTime(false);
            packet.actions = msg.ReadInt32((int)PlayerAction.Count);

            return packet;
        }

        /* the server sends data to a client */
        protected void WriteServerPacket(NetBuffer buffer)
        {
            buffer.WriteTime(false);

            WriteServerPacket(buffer, game.Field);

            List<Player> players = game.GetPlayers().list;
            for (int i = 0; i < players.Count; ++i)
            {
                WriteServerPacket(buffer, players[i]);
            }
        }

        private void WriteServerPacket(NetBuffer buffer, Field field)
        {
            int bitsForPlayerIndex = NetUtility.BitsToHoldUInt((uint)(field.GetPlayers().GetCount()-1));

            FieldCellSlot[] slots = field.GetCells().slots;
            for (int i = 0; i < slots.Length; ++i)
            {
                FieldCell staticCell = slots[i].staticCell;
                bool shouldWrite = staticCell != null && !staticCell.IsSolid();
                buffer.Write(shouldWrite);
                if (shouldWrite)
                {   
                    switch (staticCell.type)
                    {
                        case FieldCellType.Brick:
                        {
                            BrickCell brick = staticCell.AsBrick();
                            bool hasPowerup = brick.powerup != Powerups.None;

                            buffer.Write(CELL_BRICK, BITS_FOR_STATIC_CELL);
                            buffer.Write(hasPowerup);
                            if (hasPowerup)
                            {
                                buffer.Write(brick.powerup, BITS_FOR_POWERUP);
                            }
                            break;
                        }
                        case FieldCellType.Powerup:
                        {
                            PowerupCell powerup = staticCell.AsPowerup();
                            buffer.Write(CELL_POWERUP, BITS_FOR_STATIC_CELL);
                            buffer.Write(powerup.powerup, BITS_FOR_POWERUP);
                            break;
                        }
                        case FieldCellType.Flame:
                        {
                            FlameCell flame = staticCell.AsFlame();
                            buffer.Write(CELL_FLAME, BITS_FOR_STATIC_CELL);
                            buffer.Write(flame.player.GetIndex(), bitsForPlayerIndex);
                            break;
                        }
                    }
                }
            }
        }

        private void WriteServerPacket(NetBuffer buffer, Player p)
        {
            bool alive = p.IsAlive();
            buffer.Write(alive);
            if (alive)
            {
                buffer.Write(p.px);
                buffer.Write(p.py);
                buffer.Write(p.IsMoving());
                buffer.Write((byte)p.direction);
                buffer.Write(p.GetSpeed());

                // powerups
                int powerupsCount = (int)Powerups.Count;
                for (int i = 0; i < powerupsCount; ++i)
                {
                    bool hasPower = p.powerups.HasPowerup(i);
                    buffer.Write(hasPower);
                    if (hasPower)
                    {   
                        buffer.Write((byte)p.powerups.GetCount(i));
                    }
                }

                // diseases
                int diseasesCount = (int)Diseases.Count;
                for (int i = 0; i < diseasesCount; ++i)
                {
                    bool infected = p.diseases.IsInfected(i);
                    buffer.Write(infected);
                    if (infected)
                    {
                        buffer.WriteTime(NetTime.Now + p.diseases.GetInfectedRemains(i), false);
                    }
                }
            }

            Bomb[] bombs = p.bombs.array;
            for (int i = 0; i < bombs.Length; ++i)
            {
                WriteServerPacket(buffer, bombs[i]);
            }
        }

        private void WriteServerPacket(NetBuffer msg, Bomb b)
        {
            msg.Write(b.isActive);
            if (b.isActive)
            {
                msg.WriteTime(NetTime.Now + b.timeRemains, false);
                msg.Write(b.px);
                msg.Write(b.py);
                msg.Write((byte)b.direction);
                msg.Write(b.GetSpeed());
                msg.Write(b.IsJelly());
                msg.Write(b.IsTrigger());
            }
        }

        /* a client reads data from the server */
        protected ServerPacket ReadServerPacket(NetIncomingMessage msg)
        {
            ServerPacket packet;
            packet.id = msg.ReadInt32();
            packet.lastAckClientPacketId = msg.ReadInt32();
            packet.timeStamp = (float)msg.ReadTime(false);

            ReadServerPacket(msg, game.Field);

            List<Player> players = game.GetPlayers().list;
            for (int i = 0; i < players.Count; ++i)
            {
                ReadServerPacket(msg, players[i]);
            }

            return packet;
        }

        private void ReadServerPacket(NetIncomingMessage msg, Field field)
        {
            int bitsForPlayerIndex = NetUtility.BitsToHoldUInt((uint)(field.GetPlayers().GetCount() - 1));

            FieldCellSlot[] slots = field.GetCells().slots;
            for (int i = 0; i < slots.Length; ++i)
            {
                FieldCellSlot slot = slots[i];

                bool hasStaticCell = msg.ReadBoolean();
                if (hasStaticCell)
                {   
                    byte type = msg.ReadByte(BITS_FOR_STATIC_CELL);
                    switch (type)
                    {
                        case CELL_BRICK:
                        {
                            bool hasPowerup = msg.ReadBoolean();
                            int powerup = hasPowerup ? msg.ReadInt32(BITS_FOR_POWERUP) : Powerups.None;
                            break;
                        }

                        case CELL_POWERUP:
                        {
                            int powerup = msg.ReadInt32(BITS_FOR_POWERUP);
                            break;
                        }

                        case CELL_FLAME:
                        {
                            int playerIndex = msg.ReadInt32(bitsForPlayerIndex);
                            break;
                        }
                    }
                }
                else if (slot.staticCell != null && !slot.staticCell.IsSolid())
                {
                    slot.RemoveCell(slot.staticCell);
                }
            }
        }

        private void ReadServerPacket(NetIncomingMessage msg, Player p)
        {
            bool alive = msg.ReadBoolean();
            if (alive)
            {
                float px = msg.ReadFloat();
                float py = msg.ReadFloat();
                bool moving = msg.ReadBoolean();
                Direction direction = (Direction)msg.ReadByte();
                float speed = msg.ReadFloat();

                // player state
                p.UpdateFromNetwork(px, py, moving, direction, speed);

                // powerups
                int powerupsCount = (int)Powerups.Count;
                for (int i = 0; i < powerupsCount; ++i)
                {
                    bool hasPower = msg.ReadBoolean();
                    if (hasPower)
                    {
                        int count = msg.ReadByte();
                        p.powerups.SetCount(i, count);
                    }
                    else
                    {
                        p.powerups.SetCount(i, 0);
                    }
                }

                // diseases
                int diseasesCount = (int)Diseases.Count;
                for (int i = 0; i < diseasesCount; ++i)
                {
                    bool infected = msg.ReadBoolean();
                    if (infected)
                    {
                        float remains = (float)(msg.ReadTime(false) - NetTime.Now);
                        p.diseases.TryInfect(i);
                        p.diseases.SetInfectedRemains(i, remains);
                    }
                    else
                    {
                        p.diseases.TryCure(i);
                    }
                }
            }
            else if (p.IsAlive())
            {
                Field.KillPlayer(p);
            }

            Bomb[] bombs = p.bombs.array;
            for (int i = 0; i < bombs.Length; ++i)
            {
                ReadServerPacket(msg, p, bombs[i]);
            }
        }

        private void ReadServerPacket(NetIncomingMessage msg, Player p, Bomb b)
        {
            bool active = msg.ReadBoolean();
            if (active)
            {
                float remains = (float)(msg.ReadTime(false) - NetTime.Now);
                float px = msg.ReadFloat();
                float py = msg.ReadFloat();
                Direction dir = (Direction)msg.ReadByte();
                float speed = msg.ReadFloat();
                bool jelly = msg.ReadBoolean();
                bool trigger = msg.ReadBoolean();

                if (!b.isActive)
                {
                    b.player = p;
                    b.Activate();
                    game.Field.SetBomb(b);
                }

                b.timeRemains = remains;
                b.SetPos(px, py);
                b.SetSpeed(speed);
                b.SetJelly(jelly);
                b.SetTrigger(trigger);
                // TODO: jelly & trigger
            }
            else if (b.isActive)
            {
                b.Blow();
            }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Network helpers

        protected NetOutgoingMessage CreateMessage()
        {
            return GetNetwork().CreateMessage();
        }

        protected NetOutgoingMessage CreateMessage(NetworkMessageId messageId)
        {
            return GetNetwork().CreateMessage(messageId);
        }

        protected void SendMessage(NetOutgoingMessage message, NetConnection recipient, NetDeliveryMethod method = NetDeliveryMethod.Unreliable)
        {
            GetNetwork().SendMessage(message, recipient, method);
        }

        protected void SendMessage(NetworkMessageId messageId, NetDeliveryMethod method = NetDeliveryMethod.Unreliable)
        {
            GetNetwork().SendMessage(messageId, method);
        }

        protected void SendMessage(NetOutgoingMessage message, NetDeliveryMethod method = NetDeliveryMethod.Unreliable)
        {
            GetNetwork().SendMessage(message, method);
        }

        protected void SendMessage(NetworkMessageId messageId, NetConnection recipient, NetDeliveryMethod method = NetDeliveryMethod.Unreliable)
        {
            GetNetwork().SendMessage(messageId, recipient, method);
        }

        protected void RecycleMessage(NetOutgoingMessage msg)
        {
            GetNetwork().RecycleMessage(msg);
        }

        protected void RecycleMessage(NetIncomingMessage msg)
        {
            GetNetwork().RecycleMessage(msg);
        }

        protected void StopNetworkPeer()
        {
            GetNetwork().Stop();
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Peer messages

        protected void StartListeningPeerMessages(NetworkMessageId messageId, ReceivedMessageDelegate del)
        {
            GetNetwork().StartListeningMessages(messageId, del);
        }

        protected void StopListeningPeerMessages(NetworkMessageId messageId, ReceivedMessageDelegate del)
        {
            GetNetwork().StopListeningMessages(messageId, del);
        }

        protected void StopListeningPeerMessages(ReceivedMessageDelegate del)
        {
            GetNetwork().StopListeningMessages(del);
        }

        private void StopListeningPeerMessages()
        {
            GetNetwork().StopListeningAllMessages(this);
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////

        #region Peer request/response

        private void StartListeningPeerRequests()
        {
            StartListeningPeerMessages(NetworkMessageId.Request, PeerRequestReceived);
        }

        private void StopListeningPeerRequests()
        {
            StopListeningPeerMessages(NetworkMessageId.Request, PeerRequestReceived);
        }

        protected ClientRequestOperation SendPeerRequest(NetworkRequestId requestId, ClientRequestOperationDelegate reqDelegate, String message)
        {
            return SendPeerRequest(requestId, null, reqDelegate, message);
        }

        protected ClientRequestOperation SendPeerRequest(NetworkRequestId requestId, NetOutgoingMessage payloadMessage, ClientRequestOperationDelegate reqDelegate, String message)
        {
            Peer peer = GetNetwork().GetPeer();
            Debug.AssertNotNull(peer);

            ClientRequestOperation request = new ClientRequestOperation(peer, requestId, payloadMessage);
            request.requestDelegate = reqDelegate;
            request.Start();

            StartScreen(new BlockingOperationScreen(message, request));
            return request;
        }

        private void PeerRequestReceived(Peer peer, NetworkMessageId messageId, NetIncomingMessage message)
        {
            NetworkRequestId requestId = (NetworkRequestId) message.ReadByte();
            PeerRequestReceived(peer, requestId, message);
        }

        protected virtual void PeerRequestReceived(Peer peer, NetworkRequestId requestId, NetIncomingMessage message)
        {
        }

        #endregion

        protected Server GetServer()
        {
            return GetNetwork().GetServer();
        }

        protected Client GetClient()
        {
            return GetNetwork().GetClient();
        }

        protected Peer GetPeer()
        {
            return GetNetwork().GetPeer();
        }
    }
}
