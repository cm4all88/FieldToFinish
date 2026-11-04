FIELD TO FINISH -- HOW TO INSTALL OR UPDATE
===========================================

1.  Save your drawings and close Civil 3D.
2.  Double-click  Install or Update FieldToFinish.bat  in this folder.
3.  Read the window, then start Civil 3D.

That is all, the first time and every time after. The same file installs FTF,
updates it when the office approves a new release, repairs a damaged install,
and tells you "up to date" when there is nothing to do.

The FTF ribbon tabs are there when Civil 3D opens. There is nothing to load
and nothing to type: Civil 3D loads FTF by itself every time it starts.
Please do not NETLOAD FTF from this folder, or from anywhere else.

To see which FTF you have, type FTFVERSION in Civil 3D.

If you opened this folder by its \\server path rather than a drive letter, the
window may first say "UNC paths are not supported". That is Windows talking;
it does no harm, and the install carries on.

The "Previous releases" folder holds the releases this folder had before. Do
not install from there; always use the batch file in this folder.


WHAT HAPPENS WHEN YOU DOUBLE-CLICK
----------------------------------

  * It stops straight away if Civil 3D is open (close it and run it again).
  * It copies this release to your computer and checks every file against the
    list made when the release was built. A damaged or half-copied release is
    refused, and nothing changes.
  * It compares the release with the FTF you already have, and says which
    build you have, which build this folder has, and what it is going to do.
  * It keeps a copy of the FTF you had, installs the new one, and checks every
    installed file again. If anything goes wrong, the FTF you had is put back.
  * It writes a log to  %LOCALAPPDATA%\FieldToFinish\logs.

It never touches your office settings, your own settings, profiles, layer
mappings, styles, feature rules, commands, workflows or drawings.


UPDATES
-------

When the office approves a new release, it is put in this folder. Then:

  * double-click  Install or Update FieldToFinish.bat  again (Civil 3D closed), or
  * just keep working: when Civil 3D starts, FTF notices the newer release in
    this folder, and installs it when you close Civil 3D. The next time you
    open Civil 3D you are current.

There is also an  Update FieldToFinish.bat  in  %LOCALAPPDATA%\FieldToFinish
that runs the one in this folder for you.


IF SOMETHING GOES WRONG
-----------------------

"Civil 3D is open"
    Close every Civil 3D window and run it again. Civil 3D holds FTF open while
    it runs, so it cannot be replaced underneath it.

"Could not copy the release" / "cannot be reached"
    The network folder is not reachable (network, VPN). Your FTF is unchanged
    and keeps working. Try again when you are connected.

"not complete" / "not what was built" / "development build"
    This folder is not a good approved release. Nothing was changed. If a new
    release is being published right now, wait a minute and try again;
    otherwise tell the drafting lead.

The new FTF does not behave
    Close Civil 3D and double-click  Restore previous FieldToFinish.bat
    (here, or in %LOCALAPPDATA%\FieldToFinish). It puts back the FTF you had
    before the last update. Your settings stay as they are.

The tabs are not there after installing
    Close Civil 3D, double-click  Repair FTF.bat, then start Civil 3D again.
    Still missing: double-click  Check FTF.bat. It writes ftf-check.txt on your
    desktop (and a copy in %LOCALAPPDATA%\FieldToFinish\logs). Send that file
    to the drafting lead.

You have a different Civil 3D year
    This build is for Civil 3D 2024. Tell the drafting lead.


WHAT IT CHANGES ON YOUR COMPUTER
--------------------------------

No admin rights. Only these, in your own profile:

    %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle    FTF itself
    Civil 3D's own record that it loads FTF at startup
    %LOCALAPPDATA%\FieldToFinish                                  logs, backups, the local tools

Your office settings are somewhere else and are never touched by installing:

    %APPDATA%\FieldToFinish

To remove FTF, double-click  Uninstall FTF.bat  (settings are kept, and a copy
of FTF is kept so Restore previous FieldToFinish.bat can put it back).
