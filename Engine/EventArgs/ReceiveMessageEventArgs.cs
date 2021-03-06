﻿using Engine.Model.Entities;
using System;

namespace Engine
{
  [Serializable]
  public class ReceiveMessageEventArgs : EventArgs
  {
    public long MessageId { get; set; }
    public MessageType Type { get; set; }

    public string Sender { get; set; }
    public string RoomName { get; set; }

    public string Message { get; set; }

    public MessageId SystemMessage { get; set; }
    public string[] SystemMessageFormat { get; set; }

    public object State { get; set; }
  }

  public enum MessageType
  {
    Common,
    Private,
    System,
    File
  }
}
