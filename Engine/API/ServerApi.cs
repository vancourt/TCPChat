﻿using Engine.API.ClientCommands;
using Engine.API.ServerCommands;
using Engine.Model.Entities;
using Engine.Model.Server;
using Engine.Plugins;
using Engine.Plugins.Server;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;

namespace Engine.API
{
  /// <summary>
  /// Класс реазиующий стандартное серверное API.
  /// </summary>
  public sealed class ServerApi :
    CrossDomainObject,
    IApi<ServerCommandArgs>
  {
    [SecurityCritical]
    private readonly Dictionary<long, ICommand<ServerCommandArgs>> commands;

    /// <summary>
    /// Создает экземпляр API.
    /// </summary>
    [SecurityCritical]
    public ServerApi()
    {
      commands = new Dictionary<long, ICommand<ServerCommandArgs>>();

      AddCommand(new ServerRegisterCommand());
      AddCommand(new ServerUnregisterCommand());
      AddCommand(new ServerSendRoomMessageCommand());
      AddCommand(new ServerCreateRoomCommand());
      AddCommand(new ServerDeleteRoomCommand());
      AddCommand(new ServerInviteUsersCommand());
      AddCommand(new ServerKickUsersCommand());
      AddCommand(new ServerExitFromRoomCommand());
      AddCommand(new ServerRefreshRoomCommand());
      AddCommand(new ServerSetRoomAdminCommand());
      AddCommand(new ServerAddFileToRoomCommand());
      AddCommand(new ServerRemoveFileFromRoomCommand());
      AddCommand(new ServerP2PConnectRequestCommand());
      AddCommand(new ServerP2PReadyAcceptCommand());
      AddCommand(new ServerPingRequestCommand());
    }

    [SecurityCritical]
    private void AddCommand(ICommand<ServerCommandArgs> command)
    {
      commands.Add(command.Id, command);
    }

    /// <summary>
    /// Версия и имя данного API.
    /// </summary>
    public string Name
    {
      [SecuritySafeCritical]
      get { return Api.Name; }
    }

    /// <summary>
    /// Извлекает команду.
    /// </summary>
    /// <param name="message">Cообщение, по которому будет определена команда.</param>
    /// <returns>Команда.</returns>
    [SecuritySafeCritical]
    public ICommand<ServerCommandArgs> GetCommand(long id)
    {
      ICommand<ServerCommandArgs> command;
      if (commands.TryGetValue(id, out command))
        return command;

      ServerPluginCommand pluginCommand;
      if (ServerModel.Plugins.TryGetCommand(id, out pluginCommand))
        return pluginCommand;

      return ServerEmptyCommand.Empty;
    }

    /// <summary>
    /// Напрямую соединяет пользователей.
    /// </summary>
    /// <param name="senderId">Пользователь запросивший соединение.</param>
    /// <param name="senderPoint">Адрес пользователя запросившего соединение.</param>
    /// <param name="requestId">Запрвшиваемый пользователь.</param>
    /// <param name="requestPoint">Адрес запрашиваемого пользователя.</param>
    [SecuritySafeCritical]
    public void IntroduceConnections(string senderId, IPEndPoint senderPoint, string requestId, IPEndPoint requestPoint)
    {
      using (var context = ServerModel.Get())
      {
        var content = new ClientWaitPeerConnectionCommand.MessageContent
        {
          RequestPoint = requestPoint,
          SenderPoint = senderPoint,
          RemoteInfo = context.Users[senderId],
        };

        ServerModel.Server.SendMessage(requestId, ClientWaitPeerConnectionCommand.CommandId, content);
      }
    }

    /// <summary>
    /// Посылает системное сообщение клиенту.
    /// </summary>
    /// <param name="nick">Пользователь получащий сообщение.</param>
    /// <param name="roomName">Имя комнаты, для которой предназначено системное сообщение.</param>
    /// <param name="message">Сообщение.</param>
    [SecuritySafeCritical]
    public void SendSystemMessage(string nick, MessageId message, params string[] formatParams)
    {
      var sendingContent = new ClientOutSystemMessageCommand.MessageContent { Message = message, FormatParams = formatParams };
      ServerModel.Server.SendMessage(nick, ClientOutSystemMessageCommand.CommandId, sendingContent);
    }

    /// <summary>
    /// Посылает клиенту запрос на подключение к P2PService
    /// </summary>
    /// <param name="nick">Пользователь получащий запрос.</param>
    /// <param name="servicePort">Порт сервиса.</param>
    [SecuritySafeCritical]
    public void SendP2PConnectRequest(string nick, int servicePort)
    {
      var sendingContent = new ClientConnectToP2PServiceCommand.MessageContent { Port = servicePort };
      ServerModel.Server.SendMessage(nick, ClientConnectToP2PServiceCommand.CommandId, sendingContent);
    }

    /// <summary>
    /// Удаляет пользователя и закрывает соединение с ним.
    /// </summary>
    /// <param name="nick">Ник пользователя, соединение котрого будет закрыто.</param>
    [SecuritySafeCritical]
    public void RemoveUser(string nick)
    {
      ServerModel.Server.CloseConnection(nick);

      using (var server = ServerModel.Get())
      {
        foreach (string roomName in server.Rooms.Keys)
        {
          var room = server.Rooms[roomName];
          if (!room.Users.Contains(nick))
            continue;

          room.RemoveUser(nick);
          server.Users.Remove(nick);

          if (string.Equals(room.Admin, nick))
          {
            room.Admin = room.Users.FirstOrDefault();
            if (room.Admin != null)
              ServerModel.Api.SendSystemMessage(room.Admin, MessageId.RoomAdminChanged, room.Name);
          }

          var sendingContent = new ClientRoomRefreshedCommand.MessageContent
          {
            Room = room,
            Users = room.Users.Select(n => server.Users[n]).ToList()
          };

          foreach (string user in room.Users)
          {
            if (user == null)
              continue;

            ServerModel.Server.SendMessage(user, ClientRoomRefreshedCommand.CommandId, sendingContent);
          }
        }
      }

      ServerModel.Notifier.Unregistered(new ServerRegistrationEventArgs { Nick = nick });
    }
  }
}
