// 2003.09.08, 2003.12.28, 2004.10.15, 2004.12.05, 2004.12.10, 2005.01.20, 2005.02.23, 2005.07.15, 2005.08.17,2021.10.12,,2022.03.15,2022.4.20
//*********************************************************************
//** USB interface chip CH375 parallel application interface lib     ** 
//** Copyright(C)WCH 2025                                            **
//** http://wch.cn                                                   ** 
//*********************************************************************
//** CH375DLL   V3.5                                                 ** 
//** DLL for USB interface chip CH37x                                **
//** Development Environment: C, VC5.0                               **
//** Support USB chip: CH372/CH375/CH376/CH378 SPI/I2C               ** 
//** Runtime environment: Windows 2000/XP/7/8/8.1/10/11              **
//*********************************************************************
//


#ifndef		_CH375_DLL_H
#define		_CH375_DLL_H

#ifdef __cplusplus
extern "C" {
#endif

#define		mOFFSET( s, m )			( (ULONG) (ULONG_PTR)& ( ( ( s * ) 0 ) -> m ) )	// Defines a macro that gets the relative offset address of a structure member

#ifndef		max
#define		max( a, b )				( ( ( a ) > ( b ) ) ? ( a ) : ( b ) )	        // Larger value
#endif

#ifndef		min
#define		min( a, b )				( ( ( a ) < ( b ) ) ? ( a ) : ( b ) )	        // Smaller values
#endif

#ifdef		ExAllocatePool
#undef		ExAllocatePool						// Delete a memory allocation with TAG
#endif

#ifndef		NTSTATUS
typedef		LONG	NTSTATUS;					// Return status
#endif


typedef	struct	_USB_SETUP_PKT {				// USB control transmission of the establishment phase of the data request packet structure
	UCHAR			mUspReqType;				// 00H Request type
	UCHAR			mUspRequest;				// 01H Request code
	union	{
		struct	{
			UCHAR	mUspValueLow;				// 02H Value parameter low bytes
			UCHAR	mUspValueHigh;				// 03H Value parameter high bytes
		};
		USHORT		mUspValue;					// 02H-03H Value parameter
	};
	union	{
		struct	{
			UCHAR	mUspIndexLow;				// 04H Index parameter low bytes
			UCHAR	mUspIndexHigh;				// 05H Index parameter high bytes
		};
		USHORT		mUspIndex;					// 04H-05H Index parameter
	};
	USHORT			mLength;					// 06H-07H Data length of the data phase
} mUSB_SETUP_PKT, *mPUSB_SETUP_PKT;


#define		mCH375_PACKET_LENGTH	64			// Length of data packets supported by CH375
#define		mCH375_PKT_LEN_SHORT	8			// Length of short packets supported by CH375


typedef	struct	_WIN32_COMMAND {				// Define WIN32 command interface structure
	union	{
		ULONG		mFunction;					// Specify the function code or pipe number when entering
		NTSTATUS	mStatus;					// Returns operation status on output
	};
	ULONG			mLength;					// Access length, returns the length of subsequent data
	union	{
		mUSB_SETUP_PKT	mSetupPkt;				// USB controls the transmission of data requests during the setup phase
		UCHAR			mBuffer[ mCH375_PACKET_LENGTH ];	// Data buffer, 0 to 255B in length
	};
} mWIN32_COMMAND, *mPWIN32_COMMAND;


// WIN32 application layer interface command
#define		IOCTL_CH375_COMMAND		( FILE_DEVICE_UNKNOWN << 16 | FILE_ANY_ACCESS << 14 | 0x0f37 << 2 | METHOD_BUFFERED )	// Special interface

#define		mWIN32_COMMAND_HEAD		mOFFSET( mWIN32_COMMAND, mBuffer )	             // WIN32 command interface header length

#define		mCH375_MAX_NUMBER		16		    // Maximum number of CH375 connected at the same time

#define		mMAX_BUFFER_LENGTH		0x400000    // Maximum length of the data buffer is 4MB

#define		mMAX_COMMAND_LENGTH		( mWIN32_COMMAND_HEAD + mMAX_BUFFER_LENGTH )	// Maximum data length plus the length of the command structure header

#define		mDEFAULT_BUFFER_LEN		0x0400	    // Default length of the data buffer is 1024

#define		mDEFAULT_COMMAND_LEN	( mWIN32_COMMAND_HEAD + mDEFAULT_BUFFER_LEN )	// Default data length plus the length of the command structure header


// CH375 endpoint address
#define		mCH375_ENDP_INTER_UP	0x81		// Address of the interrupt data upload endpoint of CH375
#define		mCH375_ENDP_AUX_DOWN	0x01		// Address of the auxiliary data download endpoint of CH375
#define		mCH375_ENDP_DATA_UP		0x82		// Address of the data block upload endpoint of CH375
#define		mCH375_ENDP_DATA_DOWN	0x02		// Address of the data block download endpoint of the CH375


// Operation command of pipes provided by device layer interfaces
#define		mPipeDeviceCtrl			0x00000004	// Integrated control piping for CH375
#define		mPipeInterUp			0x00000005	// CH375 interrupt data upload pipeline
#define		mPipeDataUp				0x00000006	// CH375 data block upload pipeline
#define		mPipeDataDown			0x00000007	// CH375 data block download pipeline
#define		mPipeAuxDown			0x00000008	// CH375 Auxiliary data download pipeline

// Function code of the application interface
#define		mFuncNoOperation		0x00000000	// No operation
#define		mFuncGetVersion			0x00000001	// Gets the driver version number
#define		mFuncGetConfig			0x00000002	// Gets the USB device configuration descriptor
#define		mFuncSetExclusive		0x0000000b	// Set exclusive use
#define		mFuncResetDevice		0x0000000c	// Reset USB device
#define		mFuncResetPipe			0x0000000d	// Reset USB pipe
#define		mFuncAbortPipe			0x0000000e	// Cancels data requests for USB pipes
#define		mFuncSetTimeout			0x0000000f	// Set USB communication timeout
#define		mFuncBufferMode			0x00000010	// Set buffered upload mode and query the length of data in the buffer
#define		mFuncBufferModeDn		0x00000011	// Set buffered download mode and query the length of data in the buffer
#define		mFuncSetIOMode  		0x00000012	// Set whether the transmission is synchronous in buffered upload mode
#define		mFuncCtrlTranTimeOut  	0x00000018	// Set Control Transfer Time out

// USB device standard request code
#define		mUSB_CLR_FEATURE		0x01
#define		mUSB_SET_FEATURE		0x03
#define		mUSB_GET_STATUS			0x00
#define		mUSB_SET_ADDRESS		0x05
#define		mUSB_GET_DESCR			0x06
#define		mUSB_SET_DESCR			0x07
#define		mUSB_GET_CONFIG			0x08
#define		mUSB_SET_CONFIG			0x09
#define		mUSB_GET_INTERF			0x0a
#define		mUSB_SET_INTERF			0x0b
#define		mUSB_SYNC_FRAME			0x0c

// Vendor-specific request type of CH375 control transmission 
#define		mCH375_VENDOR_READ		0xc0		// CH375 Vendor-specific read operations which achieved by control transmission
#define		mCH375_VENDOR_WRITE		0x40		// CH375 Vendor-specific write operations which achieved by control transmission

// Vendor-specific request code of CH375 control transmission 
#define		mCH375_SET_CONTROL		0x51		// Output control signal
#define		mCH375_GET_STATUS		0x52		// Input status signal

// Register bit definition
#define		mBitInputRxd			0x02		// Read-only, RXD# pin input status, 1: high level, 0: low level
#define		mBitInputReq			0x04		// Read-only, REQ# pin input status, 1: high level, 0: low level


// Direct input status signal bit definition
#define		mStateRXD				0x00000200	// RXD# pin input status, 1: high level, 0: low level
#define		mStateREQ				0x00000400	// REQ# pin input status, 1: high level, 0: low level

#define		MAX_DEVICE_PATH_SIZE	128			// Maximum number of characters for device name
#define		MAX_DEVICE_ID_SIZE		64			// Maximum number of characters for device ID


typedef		VOID	( CALLBACK	* mPCH375_INT_ROUTINE ) (  // Interrupt service callback routine
	PUCHAR			iBuffer );                             // Points to a buffer, containing the current interrupt-specific data


HANDLE	WINAPI	CH375OpenDevice(        // Open the CH375 device, return the handle, if an error occurs, it will be invalid
	ULONG			iIndex );           // Specify the device serial number of CH375, 0 corresponds to the first device, -1 enables automatic search for an available device and returns its serial number


VOID	WINAPI	CH375CloseDevice(       // Close the CH375 device
	ULONG			iIndex );           // Specify the serial number of the CH375 device


ULONG	WINAPI	CH375GetVersion( );     // Get the DLL version number, return the version number


ULONG	WINAPI	CH375DriverCommand(     // Directly pass the command to the driver, return 0 if there is an error, otherwise return the data length
	ULONG			iIndex,             // Specify the serial number of the CH375 device, the DLL above V1.6 can also be the handle after the device is opened
	mPWIN32_COMMAND	ioCommand );        // Pointer to command structure
// The program returns the data length after the call, and still returns the command structure, if it is a read operation, the data is returned in the command structure,
// The returned data length is 0 when the operation fails, and the length of the entire command structure when the operation is successful. For example, if one byte is read, mWIN32_COMMAND_HEAD+1 is returned.
// Before the command structure is called, provide: pipe number or command function code, length of access data (optional), data (optional)
// After the command structure is called, it returns: the operation status code, the length of the subsequent data (optional),
// The operation status code is the code defined by WINDOWS, you can refer to NTSTATUS.H,
// The length of the subsequent data refers to the length of the data returned by the read operation. The data is stored in the subsequent buffer, and is generally 0 for the write operation.


ULONG	WINAPI	CH375GetDrvVersion( );  // Get the driver version number, return the version number, or return 0 if there is an error


BOOL	WINAPI	CH375ResetDevice(       // Reset USB device
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375GetDeviceDescr(    // Read device descriptor
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			oBuffer,            // Points to a buffer large enough to hold the descriptor
	PULONG			ioLength );         // Points to the length unit, the length to be read when input is the length to be read, and the actual read length after return


BOOL	WINAPI	CH375GetConfigDescr(    // Read configuration descriptor
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			oBuffer,            // Points to a buffer large enough to hold the descriptor
	PULONG			ioLength );         // Points to the length unit, the length to be read when input is the length to be read, and the actual read length after return


BOOL	WINAPI	CH375SetIntRoutine(     // Set interrupt service routine
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	mPCH375_INT_ROUTINE	iIntRoutine );  // Specify the interrupt service routine, if it is NULL, the interrupt service will be cancelled, otherwise the routine will be called when interrupted


BOOL	WINAPI	CH375ReadInter(         // Read interrupt data
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			oBuffer,            // Points to a buffer large enough to hold the read interrupt status data
	PULONG			ioLength );         // Points to the length unit, the length to be read when input is the length to be read, and the actual read length after return


BOOL	WINAPI	CH375AbortInter(        // Abort interrupt data read operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375ReadData(          // Read data block
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			oBuffer,            // Points to a buffer large enough to hold the read data
	PULONG			ioLength );         // Points to the length unit, the length to be read when input is the length to be read, and the actual read length after return


BOOL	WINAPI	CH375AbortRead(         // Abort data block read operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375WriteData(         // Write data block
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			iBuffer,            // Pointer to a buffer where the data to be written is placed
	PULONG			ioLength );         // Points to the length unit, the length to be written out when input is the length to be written out, and the length actually written out after return


BOOL	WINAPI	CH375AbortWrite(        // Abort block write operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375WriteRead(         // First write the standard data block(Command), then read the standard data block(Response)
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			iBuffer,            // Pointer to a buffer where the data to be written is placed, length not greater than mCH375_PACKET_LENGTH
	PVOID			oBuffer,            // Points to a buffer large enough to hold the read data,Length not less than mCH375_PACKET_LENGTH
	PULONG			ioLength );         // Points to the length unit, not greater than mCH375_PACKET_LENGTH, the length to be read when input is the length to be read, and the actual read length after return


BOOL	WINAPI	CH375GetStatus(         // Input data and status directly through CH375
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PULONG			iStatus );          // Points to a double word unit, used to save status data
// Bit 7-bit 0 correspond to D7-D0 pins of CH375, Bit 9 corresponds to RXD# pin of CH375, Bit 10 corresponds to REQ# pin of CH375


BOOL	WINAPI	CH375SetTimeout(        // Set the timeout of USB data read and write
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	ULONG			iWriteTimeout,      // Specify the timeout for USB to write data blocks, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default)
	ULONG			iReadTimeout );     // Specify the timeout for USB to read data blocks, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default)


BOOL	WINAPI	CH375WriteAuxData(      // Write Auxiliary Data
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	PVOID			iBuffer,            // Pointer to a buffer where the data to be written is placed
	PULONG			ioLength );         // Points to the length unit, the length to be written out when input is the length to be written out, and the length actually written out after return


BOOL	WINAPI	CH375SetExclusive(      // Set exclusive use of the current CH375 device
	ULONG			iIndex,             // Specify the serial number of the CH375 device
	ULONG			iExclusive );       // If the value is 0, the device can be shared. If the value is not 0, the device can be exclusively used


ULONG	WINAPI	CH375GetUsbID(          // Get the USB device ID. In the returned data, the lower 16 bits represent the Vendor ID, and the upper 16 bits represent the Product ID. If error occurs, returns 0 (an invalid ID)
	ULONG			iIndex );           // Specify the serial number of the CH375 device


PVOID	WINAPI	CH375GetDeviceName(     // Returns a buffer pointing to the CH375 device name, or NULL on error
	ULONG			iIndex );           // Specify the serial number of the CH375 device, 0 corresponds to the first device


BOOL	WINAPI	CH375SetBufUpload(      // Set internal buffered upload mode
	ULONG			iIndex,             // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEnableOrClear );   // If the value is 0, the internal buffered upload mode is disabled and direct upload is used. If the value is not 0, the internal buffered upload mode is enabled and the existing data in the buffer is cleared
// If internal buffered-upload mode is enabled, the CH375 driver creation thread automatically receives USB upload data to the internal buffer and clears the existing data in the buffer. The existing data in the buffer will be returned immediately when the application calls CH375ReadData


LONG	WINAPI	CH375QueryBufUpload(    // Query the number of existing data packets in the internal upload buffer. The number of data packets is returned successfully, or -1 is returned if an error occurs
	ULONG			iIndex );           // Specify the serial number of the CH375 device, 0 corresponds to the first device


BOOL	WINAPI	CH375SetBufDownload(    // Set the internal buffer download mode
	ULONG			iIndex,             // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEnableOrClear );   // If the value is 0, the internal buffered downtransmission mode is disabled and direct downtransmission is used. If the value is not 0, the internal buffered downtransmission mode is enabled and the existing data in the buffer is cleared
// If internal buffer download mode is enabled, when an application calls CH375WriteData, it simply puts USB downpass data into an internal buffer and returns it immediately, while threads created by the CH375 driver automatically send it to completion


LONG	WINAPI	CH375QueryBufDownload(  // Query the number of remaining packets in the internal downtransmission buffer (that have not been sent). The number of successful packets is returned, and -1 is returned if an error occurs
	ULONG			iIndex );           // Specify the serial number of the CH375 device, 0 corresponds to the first device


BOOL	WINAPI	CH375ResetInter(        // Reset an interrupted data read operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375ResetAux(          // Reset an auxiliary data write operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375ResetRead(         // Reset the data block read operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


BOOL	WINAPI	CH375ResetWrite(        // Reset a block write operation
	ULONG			iIndex );           // Specify the serial number of the CH375 device


typedef		VOID	( CALLBACK	* mPCH375_NOTIFY_ROUTINE ) (  // Device event notification callback program
	ULONG			iEventStatus );                           // Device event and current status (defined downstream): 0= device removal event, 3= device insertion event

#define		CH375_DEVICE_ARRIVAL		3		// Device insertion event, already inserted
#define		CH375_DEVICE_REMOVE_PEND	1		// Device to be pulled out
#define		CH375_DEVICE_REMOVE			0		// The device is removed


BOOL	WINAPI	CH375SetDeviceNotify(           // Set up the device event notification procedure
	ULONG					iIndex,             // Specify the serial number of the CH375 device,0 corresponds to the first device
	PCHAR					iDeviceID,          // Optional argument pointing to a string specifying the ID of the device to be monitored, terminated with \0
	mPCH375_NOTIFY_ROUTINE	iNotifyRoutine );   // Specifies a device event callback that cancels event notification if NULL, or is called when an event is detected


BOOL	WINAPI	CH375SetTimeoutEx(              // Set the timeout of USB data read and write
	ULONG			iIndex,                     // Specify the serial number of the CH375 device
	ULONG			iWriteTimeout,              // Specify the timeout for USB to write data blocks, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default); Specify the timeout duration for the non-cached download mode of CH375WriteEndP.
	ULONG			iReadTimeout,               // Specify the timeout for USB to read data blocks, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default); Specify the timeout duration for the non-cached upload mode of CH375ReadEndP.
	ULONG			iAuxTimeout,                // Specify the timeout for USB to auxiliary data download, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default)
	ULONG			iInterTimeout );            // Specify the timeout for USB to interrupt data upload, in milliseconds mS, 0xFFFFFFFF specifies no timeout (default)

// The behavior of CH375ReadEndP when reading data is as follows:
// 1. If the endpoint iEndP is set to cached upload mode:
//    The function directly reads data from the internal buffer of the driver and returns immediately.
// 2. If the cached upload mode is not set:
//    - If the USB device returns data for the IN packet (IN+DATA+ACK or IN+ACK), the function returns after reading *ioLength bytes , or returns immediately if a short packet (less than the endpoint size) or a zero-length packet is received.
//    - If the USB device returns no data for the IN packet (IN+NAK), the function enters a wait state. The wait time is determined by the iReadTimeout value which is set by the CH375SetTimeoutEx function. 
BOOL	WINAPI	CH375ReadEndP(                  // Read data blocks
	ULONG			iIndex,                     // Specify the serial number of the CH375 device
	ULONG			iEndP,                   	// Endpoint number, valid values are from 1 to 8
	PVOID			oBuffer,                    // Points to a buffer large enough to hold the read data
	PULONG			ioLength );                 // Points to the length unit, the length to be written out when input is the length to be written out, and the length actually written out after return
	
// The behavior of CH375WriteEndP when writing data is as follows:
// 1. If the endpoint iEndP is set to cached download mode:
//    The function copies the data to the driver's internal buffer and returns immediately. The driver will then send the buffered data to the USB device in sequence.
// 2. If the cached download mode is not set:
//    - If the USB device responds with an acknowledgment (ACK or NYET) for each OUT packet, the function returns after the entire *ioLength of data has been sent.
//    - If the USB device responds with NAK for any OUT packet, the function enters a wait state until the device responds with an acknowledgment. The maximum wait time is defined by the iWriteTimeout value set via the CH375SetTimeoutEx function. 
BOOL	WINAPI	CH375WriteEndP(                 // Write data blocks
	ULONG			iIndex,                     // Specify the serial number of the CH375 device
	ULONG			iEndP,                   	// Endpoint number, valid values are from 1 to 8
	PVOID			iBuffer,                    // Pointer to a buffer where the data to be written is placed
	PULONG			ioLength );                 // Points to the length unit, on input, it specifies the length to write; on return, it holds the actual length written

BOOL	WINAPI	CH375AbortEndPRead(	            // Abort block read operation
	ULONG			iIndex, 				    // Specify the serial number of the CH375 device
	ULONG			iEndP  );  					// Endpoint number, valid values are from 1 to 8
	
BOOL	WINAPI	CH375AbortEndPWrite(	        // Abort block write operation
	ULONG			iIndex, 					// Specify the serial number of the CH375 device
	ULONG			iEndP  );  					// Endpoint number, valid values are from 1 to 8

BOOL	WINAPI	CH375ResetInEndP(				// Reset an IN Endpoint data read operation
	ULONG			iIndex, 					// Specify the serial number of the CH375 device
	ULONG			iEndP  );  					// Endpoint number, valid values are from 1 to 8

BOOL	WINAPI	CH375ResetOutEndP(				// Reset an OUT Endpoint data write operation
	ULONG			iIndex, 					// Specify the serial number of the CH375 device
	ULONG			iEndP  );  					// Endpoint number, valid values are from 1 to 8

// When using CH375ReadEndP to read buffered data from the driver, the input value of the ioLength parameter must be a multiple of TransferSize
// When CH375SetBufUpload is used to enable buffered upload, the driver buffers data on endpoint 2
// When CH375SetBufUploadEx is used to enable buffered upload, the driver buffers data on endpoint iEndP	
BOOL	WINAPI	CH375SetBufUploadEx(            // Set internal buffered upload mode, the driver will read data from the iEndP endpoint and store it, with a maximum buffer size of 150 MB
	ULONG			iIndex,                     // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEnableOrClear,             // If the value is 0, the internal buffered upload mode is disabled and direct upload is used. If the value is not 0, the internal buffered upload mode is enabled and the existing data in the buffer is cleared
	ULONG			iEndP,		            	// Endpoint number, valid values are from 1 to 8
	ULONG			TransferSize );             // Buffer size per packet(URB size), maximum 4MB.It is recommended to set it as a multiple of the iEndP endpoint size.

BOOL	WINAPI	CH375QueryBufUploadEx(          // Query the number of existing data packets and total bytes in the internal upload buffer, returns TRUE on success, FALSE on failure
	ULONG			iIndex,				        // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEndP,			        	// Endpoint number, valid values are from 1 to 8
	PULONG			oTransferCount,		        // Returns the number of existing data packets in the internal buffer	
	PULONG			oTotalDataLen );            // Returns the total byte count of data packets in the internal buffer	

BOOL	WINAPI	CH375ClearBufUpload(            // Clears the internal buffer without pausing the internal buffered upload mode
	ULONG			iIndex,  			        // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEndP );                 	// Endpoint number, valid values are from 1 to 8

BOOL	WINAPI	CH375SetBufDownloadEx(          // Set internal buffered download mode, The data will be copied to the driver's internal buffer and the function will return immediately. The driver will then send the buffered data to the USB device in order.
	ULONG			iIndex,                     // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEnableOrClear,             // If the value is 0, the internal buffered downtransmission mode is disabled and direct downtransmission is used. If the value is not 0, the internal buffered downtransmission mode is enabled and the existing data in the buffer is cleared
	ULONG			iEndP,                   	// Endpoint number, valid values are from 1 to 8
	ULONG		    oMaxTransferCount);	        // Maximum number of packets to buffer, maximum 10 packets	

BOOL	WINAPI	CH375QueryBufDownloadEx(        // Query the number of remaining packets in the internal downtransmission buffer (that have not been sent), returns TRUE on success, FALSE on failure
	ULONG			iIndex,				        // Specify the serial number of the CH375 device, 0 corresponds to the first device
	ULONG			iEndP,			        	// Endpoint number, valid values are from 1 to 8
	PULONG			oTransferCount,		      	// Returns the number of remaining data packets in the internal buffer
	PULONG			oTotalDataLen );	       	// Returns the total byte count of remaining data packets in the internal buffer	

BOOL	WINAPI	CH375ControlTransfer(			// USB Control Transfer
	ULONG			iIndex,						// Specify the serial number of the CH375 device, 0 corresponds to the first device
	mPUSB_SETUP_PKT iSetupPacket,				// SETUP packet in USB control transfer
	PVOID			ioBuffer,  					// Points to a buffer that holds the data to be read or written in the control transfer.
	PULONG			ioLength,					// When inputting, the ioLength parameter specifies the number of bytes to be transferred, excluding the SETUP packet, and must be less than or equal to the size of ioBuffer. For a read request, the actual read length is returned.
	ULONG			iTimeout);					// Timeout period for control transfer, in milliseconds (mS)
#ifdef __cplusplus
}
#endif

#endif		// _CH375_DLL_H
