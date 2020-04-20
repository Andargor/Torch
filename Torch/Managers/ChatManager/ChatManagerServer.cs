﻿using System;
using System.Collections.Generic;
using System.Reflection;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Networking;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch.Managers.PatchManager;
using Torch.Utils;
using Torch.Utils.Reflected;
using VRage.Collections;
using VRage.Game;
using VRage.Network;
using VRageMath;

namespace Torch.Managers.ChatManager
{
    [PatchShim]
    internal static class ChatInterceptPatch
    {
        private static ChatManagerServer _chatManager;
        private static ChatManagerServer ChatManager => _chatManager ?? (_chatManager = TorchBase.Instance.CurrentSession.Managers.GetManager<ChatManagerServer>());

        internal static void Patch(PatchContext context)
        {
            var target = typeof(MyMultiplayerBase).GetMethod("OnChatMessageReceived_Server", BindingFlags.Static | BindingFlags.NonPublic);
            var patchMethod = typeof(ChatInterceptPatch).GetMethod(nameof(PrefixMessageProcessing), BindingFlags.Static | BindingFlags.NonPublic);
            context.GetPattern(target).Prefixes.Add(patchMethod);
        }

        private static bool PrefixMessageProcessing(ref ChatMsg msg)
        {
            var consumed = false;
            ChatManager.RaiseMessageRecieved(msg, ref consumed);
            return !consumed;
        }
    }

    public class ChatManagerServer : ChatManagerClient, IChatManagerServer
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly Logger _chatLog = LogManager.GetLogger("Chat");

        private readonly HashSet<ulong> _muted = new HashSet<ulong>();

        /// <inheritdoc />
        public ChatManagerServer(ITorchBase torchInstance) : base(torchInstance) { }

        /// <inheritdoc />
        public HashSetReader<ulong> MutedUsers => _muted;

        /// <inheritdoc />
        public event MessageProcessingDel MessageProcessing;

        /// <inheritdoc />
        public bool MuteUser(ulong steamId)
        {
            return _muted.Add(steamId);
        }

        /// <inheritdoc />
        public bool UnmuteUser(ulong steamId)
        {
            return _muted.Remove(steamId);
        }

        /// <inheritdoc />
        public void SendMessageAsOther(ulong authorId, string message, ulong targetSteamId = 0)
        {
            if (targetSteamId == Sync.MyId)
            {
                RaiseMessageRecieved(new TorchChatMessage(authorId, message, ChatChannel.Global, 0, default));
                return;
            }

            if (MyMultiplayer.Static == null)
            {
                if ((targetSteamId == MyGameService.UserId || targetSteamId == 0) && HasHud)
                    MyHud.Chat?.ShowMessage(authorId == MyGameService.UserId ? MySession.Static.LocalHumanPlayer?.DisplayName ?? "Player" : $"user_{authorId}", message);
                return;
            }

            if (MyMultiplayer.Static is MyDedicatedServerBase dedicated)
            {
                var msg = new ChatMsg() {Author = authorId, Text = message};
                _dedicatedServerBaseSendChatMessage.Invoke(ref msg);
                _dedicatedServerBaseOnChatMessage.Invoke(dedicated, new object[] {msg});
            }
        }

        public void SendMessageAsOther(string author, string message, Color color = default, ulong targetSteamId = 0, string font = MyFontEnum.White)
        {
            if (targetSteamId == Sync.MyId)
            {
                RaiseMessageRecieved(new TorchChatMessage(author, message, color, font));
                return;
            }

            if (MyMultiplayer.Static == null)
            {
                if ((targetSteamId == MyGameService.UserId || targetSteamId == 0) && HasHud)
                    MyHud.Chat?.ShowMessage(author, message, font);
                return;
            }

            var scripted = new ScriptedChatMsg()
            {
                Author = author ?? Torch.Config.ChatName,
                Text = message,
                Font = font,
                Color = color == default ? ColorUtils.TranslateColor(Torch.Config.ChatColor) : color,
                Target = Sync.Players.TryGetIdentityId(targetSteamId)
            };
            _chatLog.Info($"{author} (to {GetMemberName(targetSteamId)}): {message}");
            MyMultiplayerBase.SendScriptedChatMessage(ref scripted);
        }

        /// <inheritdoc />
        protected override bool OfflineMessageProcessor(TorchChatMessage msg)
        {
            if (MyMultiplayer.Static != null)
                return false;

            var consumed = false;
            MessageProcessing?.Invoke(msg, ref consumed);
            return consumed;
        }

        internal void RaiseMessageRecieved(ChatMsg message, ref bool consumed)
        {
            var torchMsg = new TorchChatMessage(GetMemberName(message.Author), message.Author, message.Text, (ChatChannel)message.Channel, message.TargetId, default);
            if (_muted.Contains(message.Author))
            {
                consumed = true;
                _chatLog.Warn($"MUTED USER: [{torchMsg.Channel}:{torchMsg.Target}] {torchMsg.Author}: {torchMsg.Message}");
                return;
            }

            MessageProcessing?.Invoke(torchMsg, ref consumed);

            if (!consumed)
                _chatLog.Info($"[{torchMsg.Channel}:{torchMsg.Target}] {torchMsg.Author}: {torchMsg.Message}");
        }

        public static string GetMemberName(ulong steamId)
        {
            return MyMultiplayer.Static?.GetMemberName(steamId) ?? $"user_{steamId}";
        }

#pragma warning disable 649
        private delegate void MultiplayerBaseSendChatMessageDel(ref ChatMsg arg);

        [ReflectedStaticMethod(Name = "SendChatMessage", Type = typeof(MyMultiplayerBase))]
        private static MultiplayerBaseSendChatMessageDel _dedicatedServerBaseSendChatMessage;

        // [ReflectedMethod] doesn't play well with instance methods and refs.
        [ReflectedMethodInfo(typeof(MyDedicatedServerBase), "OnChatMessage")]
        private static MethodInfo _dedicatedServerBaseOnChatMessage;
#pragma warning restore 649
    }
}