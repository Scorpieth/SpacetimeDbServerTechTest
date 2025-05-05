using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Concurrent;
using System.Data;

Identity? local_identity;

var input_queue = new ConcurrentQueue<(string Command, string Args)>();

void Main()
{
    // Initialize the `AuthToken` module
    AuthToken.Init(".spacetime_csharp_quickstart");
    // Builds and connects to the database
    DbConnection? conn = null;
    conn = ConnectToDb();
    // Registers to run in response to database events.
    RegisterCallbacks(conn);
    // Declare a threadsafe cancel token to cancel the process loop
    var cancellationTokenSource = new CancellationTokenSource();
    // Spawn a thread to call process updates and process commands
    var thread = new Thread(() => ProcessThread(conn, cancellationTokenSource.Token));
    thread.Start();
    // Handles CLI input
    InputLoop();
    // This signals the ProcessThread to stop
    cancellationTokenSource.Cancel();
    thread.Join();
}

/// The URI of the SpacetimeDB instance hosting our chat database and module.
const string HOST = "http://localhost:3000";
/// The database name we chose when we published our module.
const string DBNAME = "tech-test";
/// Load credentials from a file and connect to the database.
DbConnection ConnectToDb()
{
    DbConnection? conn = null;
    conn = DbConnection.Builder()
        .WithUri(HOST)
        .WithModuleName(DBNAME)
        .WithToken(AuthToken.Token)
        .OnConnect(OnConnected)
        .OnConnectError(OnConnectError)
        .OnDisconnect(OnDisconnected)
        .Build();
    return conn;
}

void OnConnected(DbConnection conn, Identity id, string authToken)
{
    local_identity = id;
    AuthToken.SaveToken(authToken);
    
    conn.SubscriptionBuilder().OnApplied(OnSubscriptionApplied).SubscribeToAllTables();
}

void OnSubscriptionApplied(SubscriptionEventContext ctx)
{
    Console.WriteLine("Connected");
    PrintMessagesInOrder(ctx.Db);
}

void PrintMessagesInOrder(RemoteTables tables)
{
    foreach (Message message in tables.Mesage.Iter().OrderBy(item => item.Timestamp))
    {
        PrintMessage(tables, message);
    }
}

void OnConnectError(Exception e)
{
    Console.Write($"Error while connecting: {e}");
}

void OnDisconnected(DbConnection conn, Exception? e)
{
    if (e is null)
    {
        Console.Write($"Disconnected abnormally: {e}");
        return;
    }
    Console.Write("Disconnected successfully");
}

void RegisterCallbacks(DbConnection conn)
{
    conn.Db.User.OnInsert += User_OnInsert;
    conn.Db.User.OnUpdate += User_OnUpdate;

    conn.Db.Mesage.OnInsert += Message_OnInsert;
}

string UserNameOrIdentity(User user) => user.Name ?? user.Id.ToString()[..8];

void User_OnInsert(EventContext ctx, User insertedUser)
{
    if (insertedUser.Online)
    {
        Console.WriteLine($"{UserNameOrIdentity(insertedUser)} is online");
    }
}

void User_OnUpdate(EventContext ctx, User oldUser, User newUser)
{
    if (oldUser.Name != newUser.Name)
    {
        Console.WriteLine($"{UserNameOrIdentity(oldUser)} renamed to {UserNameOrIdentity(newUser)}");
    }
    if (oldUser.Online != newUser.Online)
    {
        if (newUser.Online)
        {
            Console.WriteLine($"{UserNameOrIdentity(newUser)} connected.");
        }
        else
        {
            Console.WriteLine($"{UserNameOrIdentity(newUser)} disconnected.");
        }
    }
}

/// Our `Message.OnInsert` callback: print new messages.
void Message_OnInsert(EventContext ctx, Message insertedValue)
{
    // We are filtering out messages inserted during the subscription being applied,
    // since we will be printing those in the OnSubscriptionApplied callback,
    // where we will be able to first sort the messages before printing.
    if (ctx.Event is not Event<Reducer>.SubscribeApplied)
    {
        PrintMessage(ctx.Db, insertedValue);
    }
}

void PrintMessage(RemoteTables tables, Message message)
{
    var sender = tables.User.Id.Find(message.SenderId);
    var senderName = "unknown";
    if (sender != null)
    {
        senderName = UserNameOrIdentity(sender);
    }

    Console.WriteLine($"{senderName}: {message.Text}");
}

/// Our separate thread from main, where we can call process updates and process commands without blocking the main thread. 
void ProcessThread(DbConnection conn, CancellationToken ct)
{
    try
    {
        // loop until cancellation token
        while (!ct.IsCancellationRequested)
        {
            conn.FrameTick();

            ProcessCommands(conn.Reducers);

            Thread.Sleep(100);
        }
    }
    finally
    {
        conn.Disconnect();
    }
}

/// Read each line of standard input, and either set our name or send a message as appropriate.
void InputLoop()
{
    while (true)
    {
        var input = Console.ReadLine();
        if (input == null)
        {
            break;
        }

        if (input.StartsWith("/name "))
        {
            input_queue.Enqueue(("name", input[6..]));
        }
        else
        {
            input_queue.Enqueue(("message", input));
        }
    }
}

void ProcessCommands(RemoteReducers reducers)
{
    // process input queue commands
    while (input_queue.TryDequeue(out var command))
    {
        switch (command.Command)
        {
            case "message":
                reducers.SendMessage(command.Args);
                break;
            case "name":
                reducers.SetName(command.Args);
                break;
        }
    }
}

Main();