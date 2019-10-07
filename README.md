# Running C#/.NET Custom App on CS463 Intelligent Reader

This application demonstrates how to build custom apps using .NET/C#/CSLibrary, which can be deployed and run on the CS463 intelligent reader.

[CS463 Product Information](https://www.convergence.com.hk/products/cs463/)

## Procedues

1. Install Mono Runtime on CS463
2. Set up development environment in Visual Studio on the PC
3. Deploy and debug app on the reader

## 1. Install Mono Runtime on CS463

- Download all files under the folder mono-6.0.0.327-cs463
- Combine all files into one using the following command:
```console
cat mono-6.0.0.327-csl.tar.xz.parata* >mono-6.0.0.327-csl.tar.xz
```
- Place the compressed file on a USB drive and extract it using the command:
```console
tar xvf mono-6.0.0.327-csl.tar.xz
```
- Plug the USB drive to the reader USB host port.  
- Perform SSH (username: root password: cs463) to the reader and the USB drive should be mounted to /run/media/sda1.
- Run the following commands on CS463 terminal with root:
```console
cd mono-6.0.0.327
make install
```
- The source code package was configured and compiled previously.  Mono will then be installed on your reader under /usr/local/bin.  You can check by typing the command *mono* at the terminal to verify.

## 2. Set up development environment in Visual Studio on the PC

- Log into the web interface of the reader and go to System->Access Mode.  Change access mode to *CSL Unified API*.
- Download source code under the folder *CS463LinuxMonoDemoApp*
- Open the soluton file (.sln) using Visual Studio 2015 or later
- Edit the "Config.txt" file within the project.  If you are planning to deploy the app and run it on the reader, the IP address can stay as 127.0.0.1.  For port settings configurations, you can change the values within
- On Visual Studio, go to Tools->Extensions and Updates.  From Microsoft Marketplace, download *Mono Tools* and install as an add-on
- You will find an additonal *Mono" menu item and go to *Settings..." under the menu
- Provide the ssh log-in information and the path where the app binary will be deployed (e.g. /user/root/MonoDebugTemp)

## 3. Deploy and debug app on the reader

- Select Mono->Deploy and Debug (SSH).  Visual Studio deploy the app to the reader and debug through SSH.  You can put in breakpoint and debug the code on the device line by line


