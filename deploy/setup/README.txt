FIELD TO FINISH -- HOW TO INSTALL
=================================

1.  Close Civil 3D.
2.  Double-click  Install FTF.bat
3.  Start Civil 3D.

Two tabs are on the ribbon when it opens:

    FTF            the drawing being finished -- labels, points, drip lines,
                   tags, schedules, Dip Builder, sheets, clean up
    FTF Boundary   the record work -- recorded surveys, easements, exhibits
                   and legal drafts

There is nothing to load and nothing to type. If you would rather type,
every button is also a command: FTF opens the window, FTFDIP the Dip
Builder, FTFRECORD a recorded survey.


IF SOMETHING GOES WRONG
-----------------------

"Civil 3D is open"
    Close every Civil 3D window and run Install FTF.bat again. Civil 3D holds
    the plugin open while it is running, so it cannot be replaced underneath it.

The tabs are not there after installing
    Close Civil 3D and start it again -- the tabs are built when Civil 3D
    starts. If they are still missing, tell the drafting lead which build you
    installed (it is printed in the install window, and in version.txt beside
    this file).

You have a different Civil 3D year
    This build is for Civil 3D 2024. Tell the drafting lead.


WHAT IT CHANGES ON YOUR COMPUTER
--------------------------------

One folder, in your own profile, no admin rights:

    %APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle

Your office settings are somewhere else and are never touched by installing:

    %APPDATA%\FieldToFinish

To remove FTF, double-click  Uninstall FTF.bat  (settings are kept).
