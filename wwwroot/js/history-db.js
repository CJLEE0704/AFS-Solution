(function(global){
  let _historyRows=[];
  let _historyProjects=[];
  function requestHistory(){
    const from=document.getElementById('dcFrom')?.value||null;
    const to=document.getElementById('dcTo')?.value||null;
    const projectId=document.getElementById('dcProject')?.value||null;
    const status=document.getElementById('dcStatus')?.value||'all';
    global.sendToCSharp?.('REQUEST_HISTORY','ALL',JSON.stringify({from,to,projectId,status}));
  }

  const origRender=global.renderDhList;
  global.renderDhList=function(projs){
    if(_historyProjects.length){
      global._lastDhProjs=_historyProjects;
      return origRender?.call(this,_historyProjects);
    }
    return origRender?.apply(this,arguments);
  };

  global.dcSearch=function(){ requestHistory(); };
  global.dcGetFiltered=function(){ return _historyProjects.length ? _historyProjects : (global._lastDhProjs || []); };
  global.initDataHistory=function(){ requestHistory(); };

  const origSelDhPipe=global.selDhPipe;
  global.selDhPipe=function(projId, pipeId){
    if(_historyProjects.length){
      const prevProjects=global.projects;
      try{
        global.projects=_historyProjects;
        return origSelDhPipe?.call(this, projId, pipeId);
      }finally{
        global.projects=prevProjects;
      }
    }
    return origSelDhPipe?.apply(this, arguments);
  };

  const origMsg=global.onCSharpMessage;
  global.onCSharpMessage=function(msg){
    if(msg?.type==='historyData' && Array.isArray(msg.data)){
      _historyRows=msg.data;
      const grouped={};
      _historyRows.forEach(r=>{
        if(!grouped[r.projectId]) grouped[r.projectId]={id:r.projectId,name:r.name,addedAt:r.addedAt,fileType:r.fileType,pipes:[]};
        grouped[r.projectId].pipes.push({
          id:r.pipe.id,
          size:r.pipe.size,
          material:r.pipe.material,
          status:r.pipe.status,
          totalLength:r.pipe.totalLength||0,
          prog:r.pipe.status==='완료' ? 100 : 0,
          delay:0
        });
      });
      _historyProjects=Object.values(grouped);
      global.renderDhList?.(_historyProjects);
      const firstProj=_historyProjects[0];
      if(firstProj?.pipes?.length){
        global.selDhPipe?.(firstProj.id, firstProj.pipes[0].id);
      }
      return;
    }
    origMsg?.apply(this,arguments);
    if(msg?.type==='authResult') window.dispatchEvent(new CustomEvent('csharp-auth',{detail:msg}));
  };
})(window);
