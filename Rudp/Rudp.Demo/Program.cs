var options = new RudpOptions();
var command = DemoCommand.Parse(args);

if (string.IsNullOrEmpty(command.Mode))
{
    Console.WriteLine("usage: dotnet run -- receiver|sender");
    return;
}

if(command.Mode == "receiver")
{
    await RudpReceiver.RunAsync(command.DropFirstAck, options);
}
else if(command.Mode == "sender")
{
    if (command.Window)
    {
        var result = await RudpWindowSender.RunAsync(command.DropWindowSequence, options);
        Console.WriteLine(result.Message);
        if (!result.Success)
        {
            Console.WriteLine($"Window sender failed.");
        }
    }
    else if (command.FireOrder)
    {
        await RudpDemo.RunFireOrderSenderAsync();
    }
    else
    {
        await RudpDemo.RunStopAndWaitSenderAsync(command.DropFirstData, command.Reorder, options);
    }
}
else if(command.TestCodec)
{
    RudpDemo.RunCodecTest();
}
