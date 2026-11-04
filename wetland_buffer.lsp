;;; WLB - Wetland buffer: offset jagged polylines with smooth, tangent, rounded corners.
;;; Command: WLB
;;;  - Pick the offset distance (buffer) and an optional smoothing radius.
;;;  - Convex (outside) corners become tangent arcs of radius = offset (true buffer distance).
;;;  - Smoothing radius > 0 also rounds the concave (inside) corners with that radius
;;;    (done as offset out by D+R, then back in by R), so the buffer is never closer than D.
;;;  - Closed wetlands buffer outward automatically; open ones ask you to pick a side.
;;; Works on LWPOLYLINEs / 2D polylines / splines (not 3D polylines or feature lines - convert first).

(defun wlb-off (e dist pt / last)
  (setq last (entlast))
  (command "_.offset" dist e "_non" pt "")
  (if (not (eq last (entlast))) (entlast))
)

(defun c:WLB ( / ss i e d r pt mn mx res o1 o2
                 oldgap olderase oldcmd olderr)
  (vl-load-com)
  (setq olderr *error*
        oldgap (getvar "OFFSETGAPTYPE")
        olderase (getvar "OFFSETERASE") ; nil on some versions - guarded below
        oldcmd (getvar "CMDECHO"))
  (defun *error* (msg)
    (setvar "OFFSETGAPTYPE" oldgap)
    (if olderase (setvar "OFFSETERASE" olderase))
    (setvar "CMDECHO" oldcmd)
    (command-s "_.undo" "_end")
    (setq *error* olderr)
    (if (and msg (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*")))
      (princ (strcat "\nError: " msg)))
    (princ)
  )

  (if (not *wlb-d*) (setq *wlb-d* 50.0))
  (if (not *wlb-r*) (setq *wlb-r* 0.0))

  (prompt "\nSelect wetland polylines: ")
  (if (setq ss (ssget '((0 . "LWPOLYLINE,POLYLINE,SPLINE,ARC,CIRCLE"))))
    (progn
      (setq d (getdist (strcat "\nBuffer offset distance <" (rtos *wlb-d*) ">: ")))
      (if d (setq *wlb-d* d) (setq d *wlb-d*))
      (setq r (getdist (strcat "\nSmoothing radius for inside corners, 0 = none <" (rtos *wlb-r*) ">: ")))
      (if r (setq *wlb-r* r) (setq r *wlb-r*))

      (setvar "CMDECHO" 0)
      (setvar "OFFSETGAPTYPE" 1)   ; 1 = round (filleted, tangent arcs)
      (if olderase (setvar "OFFSETERASE" 0)) ; keep the originals
      (command "_.undo" "_begin")

      (setq i 0)
      (repeat (sslength ss)
        (setq e (ssname ss i) i (1+ i))
        ;; side point
        (if (vlax-curve-isClosed e)
          (progn
            (vla-getboundingbox (vlax-ename->vla-object e) 'mn 'mx)
            (setq mx (vlax-safearray->list mx)
                  pt (list (+ (car mx) d r 1.0) (+ (cadr mx) d r 1.0) 0.0)) ; outside the shape
          )
          (setq pt (getpoint (strcat "\nPick side to buffer for open object " (itoa i) ": ")))
        )
        (if pt
          (progn
            (if (> r 0.0)
              (if (setq o1 (wlb-off e (+ d r) pt))
                (progn
                  ;; come back in by R toward the original geometry -> rounds concave corners
                  (setq o2 (wlb-off o1 r (vlax-curve-getStartPoint e)))
                  (entdel o1)
                  (setq res o2)))
              (setq res (wlb-off e d pt)))
            (if (not res) (prompt "\n  Offset failed on one object - distance may be too large/small.")))
        )
      )

      (command "_.undo" "_end")
      (setvar "OFFSETGAPTYPE" oldgap)
      (if olderase (setvar "OFFSETERASE" olderase))
      (setvar "CMDECHO" oldcmd)
    )
  )
  (setq *error* olderr)
  (princ)
)

(princ "\nType WLB to buffer wetlands with rounded, tangent corners.")
(princ)
