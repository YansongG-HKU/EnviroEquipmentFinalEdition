# Snap7 OFFICIAL

This is the **official** Snap7 repository starting with release 1.4.3.

Previous releases are available in the <a href="https://sourceforge.net/projects/snap7/files/" target="_blank">SourceForge</a> repository.

The official site is still <a href="https://snap7.sourceforge.net/" target="_blank">here</a>.

---

To make downloading and compiling more efficient on different operating systems, the package has been split.

You will find the old folders as separate repositories, so you can download only what you are interested in.

This is the list:

| Folder | Repository   |
|----------|------------|
| /snap7/examples   | <a href="https://github.com/davenardella/snap7.examples" target="_blank">snap7.examples</a> |
| /snap7/LabVIEW | <a href="https://github.com/davenardella/snap7.LabVIEW" target="_blank">snap7.LabVIEW</a> |
| /snap7/rich-demos | <a href="https://github.com/davenardella/snap7.rich-demos" target="_blank">snap7.rich-demos</a> |
| /snap7/utility | <a href="https://github.com/davenardella/snap7.utility" target="_blank">snap7.utility</a> |

in <a href="https://github.com/davenardella/snap7.utility" target="_blank">snap7.utility</a> you will also find **HMITracer**, **clientdemo**, **serverdemo** and **partnerdemo**, already compiled for Windows, useful for testing the connection to a CPU/CP or trace what an external HMI is requesting to a PLC.

You can find further info in History.txt

---

## Build Snap7

The **/build/bin/win32** and **/build/bin/win64** folders already contain the latest Windows DLLs (and .lib), ready to be used.
For other (*nix) operating systems you need to compile snap7 to match your architecture, libc version, etc. 

To do this you need the C++ toolchain, which is usually already included in the distribution.


Otherwise, for example on Linux, you can install it with:

`sudo apt install build-essential`

### *nix OS (Linux, BSD, OSX, Solaris Intel/Sparc, MIPS)

Download and unpack this repository in your home, or use git:

`git clone https://github.com/davenardella/snap7.git`

Enter in the OS folder (snap7/build/< your OS >/), then:

#### Linux, MIPS:

`make [clean | all | install]`

#### OSX, BSD:

`gmake [clean | all | install]`

#### Solaris 32 bit using Oracle Solaris Studio

`gmake -f i386_solaris_cc.mk [clean | all | install]`

#### Solaris 64 bit using Oracle Solaris Studio

`gmake -f x86_64_solaris_cc.mk [clean | all | install]`

#### Solaris 32 bit using GNU Toolchain

`gmake -f i386_solaris_gcc.mk [clean | all | install]`

#### Solaris 64 bit using GNU Toolchain

`gmake -f x86_64_solaris_gcc.mk [clean | all | install]`

>***Note: install switch requires* sudo**

### Windows 

Open the Solution file (/build/windows/VSXXXX/VSXXXX.sln) with **Visual Studio** (Community Edition is free and fine) then build it.
