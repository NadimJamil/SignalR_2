using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using signalr.backend.Data;
using signalr.backend.Models;
using System.Security.Cryptography;

namespace signalr.backend.Hubs
{
    // On garde en mémoire les connexions actives (clé: email, valeur: userId)
    // Note: Ce n'est pas nécessaire dans le TP
    public static class UserHandler
    {
        public static Dictionary<string, string> UserConnections { get; set; } = new Dictionary<string, string>();
    }

    // L'annotation Authorize fonctionne de la même façon avec SignalR qu'avec Web API
    [Authorize]
    // Le Hub est le type de base des "contrôleurs" de SignalR
    public class ChatHub : Hub
    {
        public ApplicationDbContext _context;

        public IdentityUser CurentUser
        {
            get
            {
                // On récupère le userid à partir du Cookie qui devrait être envoyé automatiquement
                string userid = Context.UserIdentifier!;
                return _context.Users.Single(u => u.Id == userid);
            }
        }

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async override Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            UserHandler.UserConnections.Add(CurentUser.Email!, Context.UserIdentifier);

            // TODO: Envoyer des message aux clients pour les mettre à jour
            await UserList();
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            // Lors de la fermeture de la connexion, on met à jour notre dictionnary d'utilisateurs connectés
            KeyValuePair<string, string> entrie = UserHandler.UserConnections.SingleOrDefault(uc => uc.Value == Context.UserIdentifier);
            UserHandler.UserConnections.Remove(entrie.Key);

            // TODO: Envoyer un message aux clients pour les mettre à jour
            await UserList();
        }

        public async Task CreateChannel(string title)
        {
            _context.Channel.Add(new Channel { Title = title });
            await _context.SaveChangesAsync();

            // TODO: Envoyer un message aux clients pour les mettre à jour
            await Clients.All.SendAsync("ChannelList", await _context.Channel.ToListAsync());
        }

        public async Task DeleteChannel(int channelId)
        {
            Channel channel = _context.Channel.Find(channelId);

            if(channel != null)
            {
                _context.Channel.Remove(channel);
                await _context.SaveChangesAsync();
            }
            string groupName = CreateChannelGroupName(channelId);
            // Envoyer les messages nécessaires aux clients
            await Clients.Group(groupName).SendAsync("NewMessage", "[" + channel.Title + "] a été détruit");
            await Clients.Group(groupName).SendAsync("LeaveChannel");
            await Clients.All.SendAsync("ChannelList", await _context.Channel.ToListAsync());
        }

        public async Task JoinChannel(int oldChannelId, int newChannelId)
        {
            string userTag = "[" + CurentUser.Email! + "]";

            // TODO: Faire quitter le vieux canal à l'utilisateur
            if(oldChannelId > 0)
            {
                string oldGroupName = CreateChannelGroupName(oldChannelId);
                Channel channel = _context.Channel.Find(oldChannelId);
                string message = userTag + " quitte : " + channel.Title;
                await Clients.Group(oldGroupName).SendAsync("NewMessage", message);
                // TODO: Faire joindre le nouveau canal à l'utilisateur
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldGroupName);
            }

            if (newChannelId > 0)
            {
                string newGroupName = CreateChannelGroupName(newChannelId);
                await Groups.AddToGroupAsync(Context.ConnectionId, newGroupName);
                Channel channel = _context.Channel.Find(newChannelId);
                string message = userTag + " a rejoiny : " + channel.Title;
                await Clients.Group(newGroupName).SendAsync("NewMessage", message);
            }

        }

        public async Task SendMessage(string message, int channelId, string userId)
        {
            if (userId != null)
            {
                // TODO: Envoyer le message à cet utilisateur
                string message2 = "[De: " + CurentUser.Email! + "] " + message;
                await Clients.User(userId).SendAsync("NewMessage", message2);
            }
            else if (channelId != 0)
            {
                // TODO: Envoyer le message aux utilisateurs connectés à ce canal
                string groupName = CreateChannelGroupName(channelId);
                Channel channel = _context.Channel.Find(channelId);
                await Clients.Group(groupName).SendAsync("NewMessage", "[" + channel.Title + "] " + message);
            }
            else
            {
                await Clients.All.SendAsync("NewMessage", "[Tous] " + message);
            }
        }

        public async Task UserList()
        {
            await Clients.All.SendAsync("UpdateUserList", UserHandler.UserConnections.ToList());
        }

        private static string CreateChannelGroupName(int channelId)
        {
            return "Channel" + channelId;
        }
    }
}