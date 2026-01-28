using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace DoorKeybind;

public class DoorKeybindModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private ICoreServerAPI? sapi;
    private IClientNetworkChannel? clientChannel;
    private IServerNetworkChannel? serverChannel;
    private const double max_door_range = 4.0;
    private const string network_channel_id = "interactdoorskeybind";
    private const string hotkey_code = "interactdoors_interact";

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(network_channel_id).RegisterMessageType<BlockPos>();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        clientChannel = api.Network.GetChannel(network_channel_id);

        api.Input.RegisterHotKey(hotkey_code, "Interact with nearest door", GlKeys.Unknown, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler(hotkey_code, InteractWithNearestDoor);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        serverChannel = api.Network.GetChannel(network_channel_id);
        serverChannel.SetMessageHandler<BlockPos>(OnServerReceivedDoorInteract);
    }

    private bool InteractWithNearestDoor(KeyCombination keyCombination)
    {
        EntityPlayer? player = capi?.World?.Player?.Entity;
        if (capi is null || player is null)
            return false;

        BlockPos? closestDoorPos = null;
        Block? block = null;
        var closestDistance = max_door_range + 1;

        var range = (int)Math.Ceiling(max_door_range);
        var centerPos = player.Pos.AsBlockPos;

        var minPos = new BlockPos(-range, -range, -range).Add(centerPos);
        var maxPos = new BlockPos(range, range, range).Add(centerPos);

        capi.World.BlockAccessor.WalkBlocks(minPos, maxPos, (currentBlock, x, y, z) =>
        {
            if (currentBlock is BlockBaseDoor blockBaseDoor)
            {
                var blockSelection = new BlockSelection(new BlockPos(x, y, z), blockBaseDoor.GetDirection(), currentBlock);
                var openable = blockBaseDoor.DoesBehaviorAllow(capi.World, capi.World.Player, blockSelection);
                if (!openable)
                    return;
            }
            else if (currentBlock.HasBehavior<BlockBehaviorDoor>())
            {
                if (!currentBlock.GetBehavior<BlockBehaviorDoor>().handopenable)
                    return;
            }
            else
                return;

            var selectionBoxes = currentBlock.GetSelectionBoxes(capi.World.BlockAccessor, new(x, y, z));
            var blockCenter = new Vec3d(x + 0.5, y + 0.5, z + 0.5);
            var distance = selectionBoxes.Length == 0
                ? player.Pos.XYZ.DistanceTo(blockCenter)
                : selectionBoxes.Min(cuboid => player.Pos.XYZ.DistanceTo(cuboid.Center + new Vec3d(x, y, z)));

            if (distance <= max_door_range && distance <= closestDistance)
            {
                closestDoorPos = new BlockPos(x, y, z);
                block = currentBlock;
                closestDistance = distance;
            }
        },
        centerOrder: true);

        if (closestDoorPos == null || block is null)
            return false;

        clientChannel?.SendPacket(closestDoorPos);
        var blockSelection = new BlockSelection { Position = closestDoorPos, Face = BlockFacing.NORTH };
        capi.World.BlockAccessor.GetBlock(closestDoorPos).OnBlockInteractStart(capi.World, capi.World.Player, blockSelection);
        return true;
    }

    private void OnServerReceivedDoorInteract(IServerPlayer fromPlayer, BlockPos blockPos)
    {
        if (sapi == null || blockPos == null)
            return;

        var player = fromPlayer.Entity;
        if (player == null)
            return;

        var distance = player.Pos.XYZ.DistanceTo(new Vec3d(blockPos.X + 0.5, blockPos.Y + 0.5, blockPos.Z + 0.5));
        if (distance > max_door_range)
            return;

        var block = sapi.World.BlockAccessor.GetBlock(blockPos);
        if (block is not BlockBaseDoor && !block.HasBehavior<BlockBehaviorDoor>())
            return;

        var blockSelection = new BlockSelection { Position = blockPos, Face = BlockFacing.NORTH };
        block.OnBlockInteractStart(sapi.World, fromPlayer, blockSelection);
    }
}