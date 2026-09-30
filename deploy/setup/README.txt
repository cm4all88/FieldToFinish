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


UPDATES -- NOTHING TO DO
------------------------

You install once. After that FTF keeps itself the same as the office copy:

  * When Civil 3D starts, FTF looks at the office copy this was installed from.
  * If there is a newer one, the FTF window says so. Keep working -- nothing
    changes underneath you, and nothing asks you anything.
  * When you close Civil 3D, the newer FTF installs itself. The next time you
    open Civil 3D you are current.

So a new FTF reaches you the next time you open Civil 3D after closing it once.
Civil 3D holds FTF open while it runs, which is why the swap waits for the close.

To see which FTF you have, type FTFUPDATE, or click FTF Version on the FTF tab.
The FTF window shows it too.


IF SOMETHING GOES WRONG
-----------------------

"Civil 3D is open"
    Close every Civil 3D window and run Install FTF.bat again. Civil 3D holds
    the plugin open while it is running, so it cannot be replaced underneath it.

"It says a newer FTF is waiting" and it stays that way
    The update installs when Civil 3D closes. If you have closed and reopened it
    and the message is still there, type FTFUPDATE -- it says what it found and
    where it looked -- and send that to the drafting lead.

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
