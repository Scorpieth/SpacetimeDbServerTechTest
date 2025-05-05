using SpacetimeDB;

public static partial class Module
{

    [Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        name = name;

        var user = ctx.Db.User.Id.Find(ctx.Sender);
        if (user is null)
        {
            return;
        }
        user.Name = name;
        ctx.Db.User.Id.Update(user);
    }

    [Reducer]
    public static void SendMessage(ReducerContext ctx, string text)
    {
        Log.Info($"New Message from {ctx.Sender}: {text}");
        ctx.Db.Mesage.Insert(new Message()
        {
            SenderId = ctx.Sender,
            Text = text,
            Timestamp = ctx.Timestamp
        });
    }
    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        Log.Info($"Connected {ctx.Sender}");
        var user = ctx.Db.User.Id.Find(ctx.Sender);

        if (user is null)
        {
            ctx.Db.User.Insert(new User()
            {
                Id = ctx.Sender,
                Name = "Temp",
                Online = true
            });

            return;
        }

        user.Online = true;
        ctx.Db.User.Id.Update(user);
    }
    
    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        var user = ctx.Db.User.Id.Find(ctx.Sender);

        if (user is not null)
        {
            // This user should exist, so set `Online: false`.
            user.Online = false;
            ctx.Db.User.Id.Update(user);
        }
        else
        {
            // User does not exist, log warning
            Log.Warn("Warning: No user found for disconnected client.");
        }
    }
    
    
    [Table(Name = "User", Public = true)]
    public partial class User
    {
        [PrimaryKey] public Identity Id;
        public string? Name;
        public bool Online;
    }
    
    [Table(Name = "Mesage", Public = true)]
    public partial class Message
    {
        public Identity SenderId;
        public Timestamp Timestamp;
        public string Text;
    }
}