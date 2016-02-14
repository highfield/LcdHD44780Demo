using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Spi;
using Windows.Foundation;

namespace LcdHD44780
{
    internal class DriverHD44780
    {
        //some ready-to-use display configurations
        public static readonly LayoutConfig Layout16x2 = new LayoutConfig
        {
            LogicalSize = 0x0210,
            PhysicalColumns = 0x10,
            PhysicalRow0 = 0x00000000,
            PhysicalRow1 = 0x00000100,
        };

        public static readonly LayoutConfig Layout20x4 = new LayoutConfig
        {
            LogicalSize = 0x0414,
            PhysicalColumns = 0x14,
            PhysicalRow0 = 0x02000000,
            PhysicalRow1 = 0x03000100,
        };


        //bit-masks for the control pins of the LCD module
        private const int LcdEnable = 0x08;
        private const int LcdRegSel = 0x02;

        //codes defined for the HD44780 interfacing
        private const int LcdSendCommand = 0x00;
        private const int LcdSendData = 0x00 | LcdRegSel;
        private const int LcdSetFunc8 = LcdSendCommand | 0x03;  //set DL=8 bit
        private const int LcdSetFunc4 = LcdSendCommand | 0x02;  //set DL=4 bit

        //character map's address displacement between rows
        private const int AddressStep = 0x40;


        /* Important! Uncomment the code below corresponding to your target device */

        /* Uncomment for MinnowBoard Max */
        //private const string SPI_CONTROLLER_NAME = "SPI0";  /* For MinnowBoard Max, use SPI0                            */
        //private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 5 on the MBM          */
        //private const Int32 DATA_COMMAND_PIN = 3;           /* We use GPIO 3 since it's conveniently near the SPI pins  */
        //private const Int32 RESET_PIN = 4;                  /* We use GPIO 4 since it's conveniently near the SPI pins  */

        /* Uncomment for Raspberry Pi 2 */
        private const string SPI_CONTROLLER_NAME = "SPI0";    /* For Raspberry Pi 2, use SPI0                             */
        private const Int32 SPI_CHIP_SELECT_LINE = 0;         /* Line 0 maps to physical pin number 24 on the Rpi2        */
        private const Int32 DATA_COMMAND_PIN = 22;            /* We use GPIO 22 since it's conveniently near the SPI pins */
        private const Int32 RESET_PIN = 23;                   /* We use GPIO 23 since it's conveniently near the SPI pins */

        /* Uncomment for DragonBoard 410c */
        //private const string SPI_CONTROLLER_NAME = "SPI0";  /* For DragonBoard, use SPI0                                */
        //private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 12 on the DragonBoard */
        //private const Int32 DATA_COMMAND_PIN = 12;          /* We use GPIO 12 since it's conveniently near the SPI pins */
        //private const Int32 RESET_PIN = 69;                 /* We use GPIO 69 since it's conveniently near the SPI pins */


        #region Singleton pattern

        private DriverHD44780() { }


        private static DriverHD44780 _instance;

        public static DriverHD44780 Instance
        {
            get
            {
                _instance = _instance ?? new DriverHD44780();
                return _instance;
            }
        }

        #endregion


        private Timer _timer;
        private SpiDevice _spi;

        //related to the data exchange
        private byte[] _buffer;
        private int _bufferIndex = -1;

        private byte[][] _cache;

        //some physical mapping info about the LCD layout,
        //such as the rows displacement, interleaving, etc.
        private int _physicalColumns;
        private int _physicalRow0;
        private int _physicalRow1;


        /// <summary>
        /// Gets the actual number of rows managed
        /// for the connected LCD module
        /// </summary>
        public int Height { get; private set; }


        /// <summary>
        /// Gets the actual number of columns managed
        /// for the connected LCD module
        /// </summary>
        public int Width { get; private set; }


        public async Task StartAsync(
            LayoutConfig config
            )
        {
            if (this._timer != null)
            {
                throw new InvalidOperationException("The driver is already running.");
            }

            this.Height = config.LogicalSize >> 8; //rows
            this.Width = (byte)config.LogicalSize; //columns

            //a "physicalRow" is kinda row that can be written sequentially
            //to the LCD module, by taking advantage of its auto-increment
            //that is, a contiguous-address array of characters
            //each physicalRow is made of one or two "physicalBlocks"
            //a "physicalColumns" is the size of a physicalBlock
            this._physicalColumns = config.PhysicalColumns;
            this._physicalRow0 = config.PhysicalRow0;
            this._physicalRow1 = config.PhysicalRow1;

            //this indicates how many visible rows takes a single physicalRow
            int physicalBlocks = (config.PhysicalRow0 < 0x10000) ? 1 : 2;
            this._buffer = new byte[config.PhysicalColumns * physicalBlocks * 4 + 4];   //all phy-cells + 1 cmd

            this._cache = new byte[this.Height][];

            for (int i = 0; i < this.Height; i++)
            {
                this._cache[i] = new byte[this.Width];
            }

            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 1000 * 1000;
                settings.SharingMode = SpiSharingMode.Exclusive;
                settings.Mode = SpiMode.Mode0;

                string aqs = SpiDevice.GetDeviceSelector(SPI_CONTROLLER_NAME);  /* Get a selector string that will return all SPI controllers on the system */
                var dis = await DeviceInformation.FindAllAsync(aqs);            /* Find the SPI bus controller devices with our selector string             */
                _spi = await SpiDevice.FromIdAsync(dis[0].Id, settings);        /* Create an SpiDevice with our bus controller and SPI settings             */
                if (_spi == null)
                {
                    //we have a problem :)
                    return;
                }

                /**
                 * According to the HD44780 specs (page 46), the init for
                 * 4-bit interface should be done as follows:
                 * - the chip could be either in the 8-bit mode, or in the 4-bit
                 *   depending on the power-on status
                 * - send just a byte, then wait at least 4.1 ms
                 * - send another a byte, then wait at least 100 us
                 *   doing so the chip is always under control, regardless its mode
                 * - send one byte, and immediately the byte for the 4-bit mode
                 * - the chip is now working in 4-bit mode
                 **/
                this._bufferIndex = 0;
                this.WriteCommand(LcdSetFunc8);
                this.Send();
                await Task.Delay(5);    //this yields a small pause

                this.WriteCommand(LcdSetFunc8);
                this.Send();
                await Task.Delay(1);    //this yields a small pause

                this.WriteCommand(LcdSetFunc8);
                this.WriteCommand(LcdSetFunc4);

                //at this point the HD44780 is working in 4-bit mode

                //complete the init
                WriteCommand(0x28); //set 2 rows (and 4-bit mode again)
                WriteCommand(0x0C); //turn on the display
                WriteCommand(0x06); //inc cursor, but don't shift the display
                WriteCommand(0x02); //return home

                this.Send();
                this.Clear();

                //start the rendering timer
                this._timer = new Timer(this.TimerCallback, null, 200, 200);
            }
            catch (Exception e)
            {
                //Debug.Print("Error: " + e.Message);
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }


        public void Stop()
        {
            if (this._timer != null)
            {
                this._timer.Dispose();
                this._timer = null;
            }
        }


        public void Clear()
        {
            //fill the video buffer with spaces
            for (int i = 0; i < this.Height; i++)
            {
                for(int k=0; k<this.Width; k++)
                {
                    this._cache[i][k] = 0x20;
                }
            }

            this.Invalidate();
        }


        public void DrawString(
            string text,
            Point pt
            )
        {
            int length = text.Length;

            if (text == null ||
                length == 0 ||
                pt.X >= this.Width ||
                pt.Y < 0 ||
                pt.Y >= this.Height
                )
            {
                //can skip the process
                return;
            }

            for (int i = 0; i < length; i++)
            {
                int xx = (int)pt.X + i;
                if (xx >= 0 && xx < this.Width)
                {
                    this._cache[(int)pt.Y][xx] = (byte)text[i];
                }
            }

            this.Invalidate();
        }


        /// <summary>
        /// Defines the pattern for a custom character
        /// </summary>
        /// <param name="code">The character code which the pattern is related to</param>
        /// <param name="pattern">The bit pattern which defines the aspect</param>
        /// <remarks>
        /// There are up to 8 codes available for custom characters:
        /// the codes span from 0 to 7, inclusive.
        /// Upon the display type, a character can be composed either
        /// by a 5x8 pixel matrix (most common), or a 5x10 pixel matrix.
        /// However, the most bottom row is reserved for cursor.
        /// Also see the HD44780 datasheet
        /// </remarks>
        /// <example>
        /// Suppose you would like to define the following
        /// 5x7 custom pattern for the code 0x02:
        /// 
        ///     #####
        ///     #
        ///     #
        ///     ###
        ///     #
        ///     #
        ///     #
        ///     
        /// Note: each '#' symbol is actually a pixel
        /// 
        /// The related code to define the pattern will be:
        /// <code>
        ///     driver.DefineCustomCharacter(
        ///         0x02,   //the address of the character
        ///         new byte[7] { 0x1F, 0x10, 0x10, 0x1C, 0x10, 0x10, 0x10 }
        ///         );
        /// </code>
        /// </example>
        public void DefineCustomCharacter(
            int code,
            byte[] pattern
            )
        {
            //checks for driver initialization
            if (this._bufferIndex < 0)
            {
                throw new InvalidOperationException("Driver not initialized.");
            }

            int address = 0x40 + ((code << 3) & 0x38);
            WriteCommand(address);

            int count = pattern.Length;
            if (count > 10)
            {
                count = 10;
            }

            for (int i = 0; i < count; i++)
            {
                WriteData(pattern[i]);
            }

            this.Send();
        }


        /// <summary>
        /// Performs a dump of single physical row
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="block0"></param>
        /// <param name="block1"></param>
        private void DumpPhysicalRow(
            byte[][] cache,
            int block0,
            int block1
            )
        {
            this.DumpPhysicalBlock(
                cache[block0 >> 8],
                (byte)block0
                );

            if (block1 != 0)
            {
                this.DumpPhysicalBlock(
                    cache[block1 >> 8],
                    (byte)block1
                    );
            }

            this.Send();
        }


        /// <summary>
        /// Deploys the data for the dumping of a single physical block
        /// </summary>
        /// <param name="vrow"></param>
        /// <param name="offset"></param>
        private void DumpPhysicalBlock(
            byte[] vrow,
            int offset
            )
        {
            for (int idx = offset, count = this._physicalColumns + offset; idx < count; idx++)
            {
                this.WriteData(vrow[idx]);
            }
        }


        /// <summary>
        /// Perform the buffer transfer to the LCD module
        /// </summary>
        /// <remarks>
        /// This function resets the buffer index
        /// </remarks>
        private void Send()
        {
            try
            {
                var transit = new byte[1];
                for (int i = 0; i < this._bufferIndex; i++)
                {
                    transit[0] = this._buffer[i];
                    this._spi.Write(transit);
                }
            }
            finally
            {
                //reset buffer index
                this._bufferIndex = 0;
            }
        }


        /// <summary>
        /// Compose the bytes-pattern for sending the specified command
        /// to the LCD module
        /// </summary>
        /// <param name="value">The command to be sent</param>
        private void WriteCommand(
            int value
            )
        {
            this.WriteByte((value & 0xF0) | LcdSendCommand);
            this.WriteByte((value << 4) | LcdSendCommand);
        }


        /// <summary>
        /// Compose the bytes-pattern for sending the specified data
        /// to the LCD module
        /// </summary>
        /// <param name="value">The data to be sent</param>
        private void WriteData(
            int value
            )
        {
            this.WriteByte((value & 0xF0) | LcdSendData);
            this.WriteByte((value << 4) | LcdSendData);
        }


        /// <summary>
        /// Compose the bytes-pattern for latching a nibble (lower 4 bits)
        /// to the LCD module (ref to the 74HC595 schematic)
        /// </summary>
        /// <param name="data">The encoded nibble to be sent</param>
        private void WriteByte(
            int data
            )
        {
            this._buffer[this._bufferIndex + 0] = (byte)(data | LcdEnable);
            this._buffer[this._bufferIndex + 1] = (byte)data;

            this._bufferIndex += 2;
        }


        #region Rendering clock

        private int _lastHash;
        private int _hash;


        /// <summary>
        /// Changes the local hash so that the host may be notified
        /// </summary>
        public void Invalidate()
        {
            this._hash++;
        }


        private void TimerCallback(object state)
        {
            //checks whether something in the cache has changed
            if (this._hash == this._lastHash) return;
            this._lastHash = this._hash;

            //physical row #0 (always present)
            int row = this._physicalRow0;

            int address = 0x80;
            WriteCommand(address);

            this.DumpPhysicalRow(
                this._cache,
                (short)row,
                (row >> 16)
                );

            //physical row #1
            if ((row = this._physicalRow1) != 0)
            {
                address += AddressStep;
                WriteCommand(address);

                this.DumpPhysicalRow(
                    this._cache,
                    (short)row,
                    (row >> 16)
                    );
            }
        }

        #endregion


        #region Driver config

        public struct LayoutConfig
        {
            public short LogicalSize;
            public byte PhysicalColumns;
            public int PhysicalRow0;
            public int PhysicalRow1;
        }

        #endregion

    }
}
