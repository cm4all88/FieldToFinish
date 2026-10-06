// Extracted verbatim from the Survey Schedule (38b41a3b-Survey_Schedule_PSO_3.html, v66) by extract.js.
// sha256 8d06a5c8905e9a0fa718ffaf73bb9b42dd3103ac80c5e468294fe9a34c539c08
// Do not edit by hand: re-extract when the Schedule changes. Crew Upload's ScheduleAssembler.cs must give
// the same result as this code; the compatibility test checks that.
function emptyFeed(id){return{pmId:id,profile:{pinHash:null},projects:[],assignments:[],notes:[]};}
function assembleState(fs0){
  const s={meta:(state&&state.meta)?state.meta:{version:2,dirtySinceExport:false},
    groups:fs0.master.groups||[],employees:fs0.master.employees||[],
    pms:(fs0.master.pms||[]).map(p=>({...p})),
    customHolidays:fs0.master.customHolidays||[],
    patterns:fs0.master.patterns||{alternating:[],weeks:[]},
    links:fs0.master.links||[],
    weather:fs0.master.weather||{on:true,lat:47.6062,lon:-122.3321,label:"Seattle",unit:"F"},
    projRoot:fs0.master.projRoot||"",
    ackConflicts:fs0.master.ackConflicts||[],
    activities:fs0.master.activities||undefined,
    anticipated:fs0.master.anticipated||[],
    traffic:fs0.master.traffic||{code:"",picks:[]},
    mail:fs0.master.mail||{domain:"",pattern:"first.last"},
    features:fs0.master.features||{reports:false},
    projects:[],assignments:[],notes:[]};
  for(const pm of s.pms){
    const f=fs0.feeds[pm.id]||emptyFeed(pm.id);
    if(f.profile&&f.profile.pinHash)pm.pinHash=f.profile.pinHash;
    s.projects.push(...(f.projects||[]));
    s.assignments.push(...(f.assignments||[]));
    s.notes.push(...(f.notes||[]));
  }
  const memPending=state?state.assignments.filter(a=>a.approval==="pending"):[];
  const finalReq=(fs0.requests||[]).filter(r=>!reqRemoved.has(r.id));
  memPending.forEach(p=>{if(reqAdded.has(p.id)&&!finalReq.find(x=>x.id===p.id))finalReq.push(p);});
  s.assignments.push(...finalReq);
  // cross-PM overrides: newest change per entry wins (disk ops merged with my session ops)
  const ovr={};
  (fs0.overrides||[]).forEach(o=>{if(!ovr[o.id]||o.at>ovr[o.id].at)ovr[o.id]=o;});
  Object.values(myOverrides).forEach(o=>{if(!ovr[o.id]||o.at>ovr[o.id].at)ovr[o.id]=o;});
  s.progress={};
  (fs0.progress||[]).forEach(r=>{
    const cur=s.progress[r.id];
    if(!cur||(r.at||"")>(cur.at||""))s.progress[r.id]=r;
  });
  Object.values(ovr).forEach(o=>{
    if(o.kind==="proj"){
      if(!o.data)return;
      const pi=s.projects.findIndex(p=>p.id===o.data.id);
      const pcur=pi>=0?s.projects[pi]:null;
      if(pcur&&o.at<=(pcur.updatedAt||""))return;   // owner's newer settings win
      if(pi>=0)s.projects[pi]=o.data;else s.projects.push(o.data);
      return;
    }
    const idx=s.assignments.findIndex(a=>a.id===o.id);
    const cur=idx>=0?s.assignments[idx]:null;
    const curAt=(cur&&cur.updatedAt)||"";
    if(o.at<=curAt)return;                      // owner has since made a newer edit — theirs wins
    if(o.op==="delete"){if(idx>=0)s.assignments.splice(idx,1);}
    else if(o.data){if(idx>=0)s.assignments[idx]=o.data;else s.assignments.push(o.data);}
  });
  return s;
}
