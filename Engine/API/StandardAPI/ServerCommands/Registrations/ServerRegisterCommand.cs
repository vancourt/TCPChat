﻿using Engine.API.StandardAPI.ClientCommands;
using Engine.Model.Entities;
using Engine.Model.Server;
using Engine.Network.Connections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Engine.API.StandardAPI.ServerCommands
{
  class ServerRegisterCommand :
      BaseServerCommand,
      IServerAPICommand
  {
    public void Run(ServerCommandArgs args)
    {
      MessageContent receivedContent = GetContentFromMessage<MessageContent>(args.Message);

      if (receivedContent.User == null)
        throw new ArgumentNullException("User");

      if (receivedContent.User.Nick == null)
        throw new ArgumentNullException("User.Nick");

      if (receivedContent.User.Nick.Contains(Connection.TempConnectionPrefix))
      {
        SendFail(args.ConnectionId, "Соединение не может быть зарегистрировано с таким ником. Выберите другой.");
        return;
      }

      using (var server = ServerModel.Get())
      {
        Room room = server.Rooms[ServerModel.MainRoomName];    
        bool userExist = room.Users.Any(nick => string.Equals(receivedContent.User.Nick, nick));

        if (userExist)
        {
          SendFail(args.ConnectionId, "Соединение не может быть зарегистрировано с таким ником. Он занят.");
          return;
        }
        else
        {
          ServerModel.Server.RegisterConnection(args.ConnectionId, receivedContent.User.Nick, receivedContent.OpenKey);

          server.Users.Add(receivedContent.User.Nick, receivedContent.User);
          room.Add(receivedContent.User.Nick);

          var regResponseContent = new ClientRegistrationResponseCommand.MessageContent { Registered = true };
          ServerModel.Server.SendMessage(args.ConnectionId, ClientRegistrationResponseCommand.Id, regResponseContent);

          var sendingContent = new ClientRoomRefreshedCommand.MessageContent
          {
            Room = room,
            Users = room.Users.Select(nick => server.Users[nick]).ToList()
          };

          foreach (var connectionId in ServerModel.Server.GetConnetionsIds())
            ServerModel.Server.SendMessage(connectionId, ClientRoomRefreshedCommand.Id, sendingContent);
        }
      }
    }

    private void SendFail(string connectionId, string message)
    {
      var regResponseContent = new ClientRegistrationResponseCommand.MessageContent { Registered = false, Message = message };
      ServerModel.Server.SendMessage(connectionId, ClientRegistrationResponseCommand.Id, regResponseContent);
      ServerModel.API.CloseConnection(connectionId);
    }

    [Serializable]
    public class MessageContent
    {
      RSAParameters openKey;
      User user;

      public RSAParameters OpenKey { get { return openKey; } set { openKey = value; } }
      public User User { get { return user; } set { user = value; } }
    }

    public const ushort Id = (ushort)ServerCommand.Register;
  }
}