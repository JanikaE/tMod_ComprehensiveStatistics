using System;
using System.IO;
using Terraria.ModLoader;

namespace Terraria
{
    /// <summary>捕获客户端当前世界的真实值，测试服务器随后独立接受或拒绝该区域。</summary>
    internal static partial class NetMessage
    {
        /// <summary>模拟 PickTile 在 WorldGen.KillTile 返回后发送的原版采掘包。</summary>
        internal static void SendKill(int x,int y)
        {
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write((byte)17);writer.Write((byte)0);writer.Write((short)x);writer.Write((short)y);writer.Write((short)0);writer.Write((byte)0);
            ModPacket.Sent.Add((-1,stream.ToArray()));
        }
        /// <summary>头部与已审计原版 TileSquare 一致；正文只用于模拟真实接收后的格位写入。</summary>
        internal static void SendTileSquare(int who,int x,int y,int width,int height)
        {
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write((byte)20);writer.Write((short)x);writer.Write((short)y);writer.Write((byte)width);writer.Write((byte)height);writer.Write((byte)0);
            for(int i=x;i<x+width;i++)for(int j=y;j<y+height;j++) WriteTile(writer,Main.Tiles[i,j]);
            ModPacket.Sent.Add((-1,stream.ToArray()));
        }

        /// <summary>模拟原版处理器成功接受区域包，生产观察器仍在 GetData 两侧采集。</summary>
        internal static void Accept(byte[] bytes)
        {
            using var reader=new BinaryReader(new MemoryStream(bytes));
            int message=reader.ReadByte();
            if(message==48){int i=reader.ReadInt16(),j=reader.ReadInt16();Main.Tiles[i,j].LiquidAmount=reader.ReadByte();
                Main.Tiles[i,j].LiquidType=reader.ReadByte();WorldGen.SquareTileFrame(i,j);return;}
            if(message==17){reader.ReadByte();int i=reader.ReadInt16(),j=reader.ReadInt16();WorldGen.KillTile(i,j);return;}
            if(message!=20)throw new Exception("Unexpected native message.");
            int x=reader.ReadInt16(),y=reader.ReadInt16(),width=reader.ReadByte(),height=reader.ReadByte();reader.ReadByte();
            for(int i=x;i<x+width;i++)for(int j=y;j<y+height;j++)Main.Tiles[i,j]=ReadTile(reader);
        }

        /// <summary>保存测试读取的全部内容身份和组件位，避免共享可变 Tile 引用。</summary>
        internal static void WriteTile(BinaryWriter writer,Tile tile)
        {
            writer.Write(tile.HasTile);writer.Write(tile.TileType);writer.Write(tile.WallType);writer.Write(tile.Style);
            writer.Write(tile.OriginX);writer.Write(tile.OriginY);writer.Write(tile.RedWire);writer.Write(tile.BlueWire);
            writer.Write(tile.GreenWire);writer.Write(tile.YellowWire);writer.Write(tile.HasActuator);writer.Write(tile.Slope);
        }

        /// <summary>为服务器恢复独立的模拟格位。</summary>
        internal static Tile ReadTile(BinaryReader reader)=>new Tile
        {
            HasTile=reader.ReadBoolean(),TileType=reader.ReadInt32(),WallType=reader.ReadInt32(),Style=reader.ReadInt32(),
            OriginX=reader.ReadInt32(),OriginY=reader.ReadInt32(),RedWire=reader.ReadBoolean(),BlueWire=reader.ReadBoolean(),
            GreenWire=reader.ReadBoolean(),YellowWire=reader.ReadBoolean(),HasActuator=reader.ReadBoolean(),Slope=reader.ReadInt32()
        };
    }
}
