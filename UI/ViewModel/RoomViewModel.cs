﻿using Engine;
using Engine.Model.Client;
using Engine.Model.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Windows.Input;
using UI.Dialogs;
using UI.Infrastructure;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Keys = System.Windows.Forms.Keys;

namespace UI.ViewModel
{
  public class RoomViewModel : BaseViewModel
  {
    #region consts
    private const string InviteInRoomTitleKey = "roomViewModel-inviteInRoomTitle";
    private const string KickFormRoomTitleKey = "roomViewModel-kickFormRoomTitle";
    private const string NoBodyToInviteKey = "roomViewModel-nobodyToInvite";
    private const string AllInRoomKey = "roomViewModel-allInRoom";

    private const string FileDialogFilter = "Все файлы|*.*";

    private const int MessagesLimit = 200;
    private const int CountToDelete = 100;
    #endregion

    #region fields
    private bool updated;
    private bool messagesAutoScroll;
    private string message;
    private int messageCaretIndex;
    private UserViewModel allInRoom;

    private ObservableCollection<MessageViewModel> messages;
    private HashSet<long> messageIds;

    private long? messageId;
    private long? SelectedMessageId
    {
      get { return messageId; }
      set { SetValue(value, "IsMessageSelected", v => messageId = v); }
    }
    #endregion

    #region commands
    public ICommand InviteInRoomCommand { get; private set; }
    public ICommand KickFromRoomCommand { get; private set; }
    public ICommand SendMessageCommand { get; private set; }
    public ICommand PastReturnCommand { get; private set; }
    public ICommand ClearSelectedMessageCommand { get; private set; }
    public ICommand AddFileCommand { get; private set; }
    #endregion

    #region properties
    public Room Description { get; private set; }
    public UserViewModel SelectedReceiver { get; set; }
    public MainViewModel MainViewModel { get; private set; }
    public bool IsMessageSelected { get { return SelectedMessageId != null; } } // OnProperyChanged called from SelectedMessageId

    public bool MessagesAutoScroll
    {
      get { return messagesAutoScroll; }
      set
      {
        messagesAutoScroll = value;
        OnPropertyChanged("MessagesAutoScroll");

        if (value == true)
          MessagesAutoScroll = false;
      }
    }

    public bool Updated
    {
      get { return updated; }
      set { SetValue(value, "Updated", v => updated = v); }
    }

    public string Name
    {
      get { return Description.Name; }
    }

    public string Message
    {
      get { return message; }
      set 
      { 
        SetValue(value, "Message", v => message = v);
        if (value == string.Empty)
          SelectedMessageId = null;
      }
    }

    public int MessageCaretIndex
    {
      get { return messageCaretIndex; }
      set { SetValue(value, "MessageCaretIndex", v => messageCaretIndex = v); }
    }

    public RoomType Type { get { return Description is VoiceRoom ? RoomType.Voice : RoomType.Chat; } }

    public IEnumerable<UserViewModel> Receivers
    {
      get
      {
        yield return allInRoom;
        foreach (var user in MainViewModel.AllUsers)
          if (!user.IsClient)
            yield return user;

        SelectedReceiver = allInRoom;
      }
    }

    public ObservableCollection<MessageViewModel> Messages
    {
      get { return messages; }
      set { SetValue(value, "Messages", v => messages = v); }
    }

    public ObservableCollection<UserViewModel> Users { get; private set; }
    #endregion

    #region constructors
    public RoomViewModel(MainViewModel main, Room room, IList<User> users)
      : base(main, true)
    {
      Description = room;
      MainViewModel = main;
      Messages = new ObservableCollection<MessageViewModel>();

      allInRoom = new UserViewModel(AllInRoomKey, new User(string.Empty, Color.Black), this);
      messageIds = new HashSet<long>();
      Users = new ObservableCollection<UserViewModel>(users == null
        ? Enumerable.Empty<UserViewModel>()
        : users.Select(user => new UserViewModel(user, this)));

      SendMessageCommand = new Command(SendMessage, _ => ClientModel.Client != null);
      PastReturnCommand = new Command(PastReturn);
      AddFileCommand = new Command(AddFile, _ => ClientModel.Client != null);
      InviteInRoomCommand = new Command(InviteInRoom, _ => ClientModel.Client != null);
      KickFromRoomCommand = new Command(KickFromRoom, _ => ClientModel.Client != null);
      ClearSelectedMessageCommand = new Command(ClearSelectedMessage, _ => ClientModel.Client != null);

      MainViewModel.AllUsers.CollectionChanged += AllUsersCollectionChanged;
      NotifierContext.ReceiveMessage += ClientReceiveMessage;
      NotifierContext.RoomRefreshed += ClientRoomRefreshed;
    }

    protected override void DisposeManagedResources()
    {
      base.DisposeManagedResources();

      foreach (var user in Users)
        user.Dispose();
      Users.Clear();

      foreach (var message in Messages)
        message.Dispose(); 
      Messages.Clear();

      MainViewModel.AllUsers.CollectionChanged -= AllUsersCollectionChanged;

      if (NotifierContext != null)
      {
        NotifierContext.ReceiveMessage -= ClientReceiveMessage;
        NotifierContext.RoomRefreshed -= ClientRoomRefreshed;
      }
    }
    #endregion

    #region methods
    public void EditMessage(MessageViewModel message)
    {
      SelectedMessageId = message.MessageId;
      Message = message.Text;
    }

    public void AddSystemMessage(string message)
    {
      AddMessage(new MessageViewModel(message, this));
    }

    public void AddMessage(long messageId, UserViewModel sender, string message)
    {
      AddMessage(new MessageViewModel(messageId, sender, null, message, false, this));
    }

    public void AddPrivateMessage(UserViewModel sender, UserViewModel receiver, string message)
    {
      AddMessage(new MessageViewModel(Room.SpecificMessageId, sender, receiver, message, true, this));
    }

    public void AddFileMessage(UserViewModel sender, FileDescription file)
    {
      AddMessage(new MessageViewModel(sender, file.Name, file, this));
    }

    private void AddMessage(MessageViewModel message)
    {
      TryClearMessages();

      if (message.MessageId == Room.SpecificMessageId || messageIds.Add(message.MessageId))
        Messages.Add(message);
      else
      {
        var existingMessage = Messages.First(m => m.MessageId == message.MessageId);
        existingMessage.Text = message.Text;
      }    

      MessagesAutoScroll = true;
    }

    private void TryClearMessages()
    {
      if (Messages.Count < MessagesLimit)
        return;

      for (int i = 0; i < CountToDelete; i++)
        Messages[i].Dispose();

      var leftMessages = Messages.Skip(CountToDelete);
      var deletingMessages = Messages.Take(CountToDelete);

      messageIds.ExceptWith(deletingMessages.Select(m => m.MessageId));
      Messages = new ObservableCollection<MessageViewModel>(leftMessages);
    }
    #endregion

    #region command methods
    private void SendMessage(object obj)
    {
      if (Message == string.Empty)
        return;

      try
      {
        if (ClientModel.Api == null || !ClientModel.Client.IsConnected)
          return;

        if (ReferenceEquals(allInRoom, SelectedReceiver))
          ClientModel.Api.SendMessage(SelectedMessageId, Message, Name);
        else
        {
          ClientModel.Api.SendPrivateMessage(SelectedReceiver.Nick, Message);
          var sender = MainViewModel.AllUsers.Single(uvm => uvm.Info.Equals(ClientModel.Client.Id));
          AddPrivateMessage(sender, SelectedReceiver, Message);
        }
      }
      catch (SocketException se)
      {
        AddSystemMessage(se.Message);
      }
      finally
      {
        SelectedMessageId = null;
        Message = string.Empty;
      }
    }

    private void PastReturn(object obj)
    {
      Message += Environment.NewLine;
      MessageCaretIndex = Message.Length;
    }

    private void AddFile(object obj)
    {
      try
      {
        var openDialog = new OpenFileDialog();
        openDialog.Filter = FileDialogFilter;

        if (openDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && ClientModel.Api != null)
          ClientModel.Api.AddFileToRoom(Name, openDialog.FileName);
      }
      catch (SocketException se)
      {
        AddSystemMessage(se.Message);
      }
    }

    private void InviteInRoom(object obj)
    {
      try
      {
        var availableUsers = MainViewModel.AllUsers.Except(Users);
        if (!availableUsers.Any())
        {
          AddSystemMessage(Localizer.Instance.Localize(NoBodyToInviteKey));
          return;
        }

        var dialog = new UsersOperationDialog(InviteInRoomTitleKey, availableUsers);
        if (dialog.ShowDialog() == true && ClientModel.Api != null)
          ClientModel.Api.InviteUsers(Name, dialog.Users);
      }
      catch (SocketException se)
      {
        AddSystemMessage(se.Message);
      }
    }

    private void KickFromRoom(object obj)
    {
      try
      {
        var dialog = new UsersOperationDialog(KickFormRoomTitleKey, Users);
        if (dialog.ShowDialog() == true && ClientModel.Api != null)
          ClientModel.Api.KickUsers(Name, dialog.Users);
      }
      catch (SocketException se)
      {
        AddSystemMessage(se.Message);
      }
    }

    private void ClearSelectedMessage(object obj)
    {
      if (SelectedMessageId == null)
        return;

      SelectedMessageId = null;
      Message = string.Empty;
    }
    #endregion

    #region client methods
    private void AllUsersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
      OnPropertyChanged("Receivers");
    }

    private void ClientReceiveMessage(object sender, ReceiveMessageEventArgs e)
    {
      if (e.RoomName != Name)
        return;

      Dispatcher.BeginInvoke(new Action<ReceiveMessageEventArgs>(args =>
      {
        var senderUser = MainViewModel.AllUsers.Single(uvm => uvm.Info.Nick == args.Sender);

        switch (args.Type)
        {
          case MessageType.Common:
            AddMessage(args.MessageId, senderUser, args.Message);
            break;

          case MessageType.File:
            AddFileMessage(senderUser, (FileDescription)args.State);
            break;
        }

        if (Name != MainViewModel.SelectedRoom.Name)
          Updated = true;

        MainViewModel.Alert();
      }), e);
    }

    private void ClientRoomRefreshed(object sender, RoomEventArgs e)
    {
      if (e.Room.Name != Name)
        return;

      Dispatcher.BeginInvoke(new Action<RoomEventArgs>(args =>
      {
        Description = args.Room;

        foreach (var user in Users)
          user.Dispose();

        Users.Clear();

        foreach (string user in Description.Users)
          Users.Add(new UserViewModel(args.Users.Find(u => string.Equals(u.Nick, user)), this));

        OnPropertyChanged("Name");
        OnPropertyChanged("Admin");
        OnPropertyChanged("Users");
      }), e);
    }
    #endregion
  }
}
