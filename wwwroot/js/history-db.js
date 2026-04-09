(function(global){
  let _historyRows=[];
  function requestHistory(){
    const from=document.getElementById('dcFrom')?.value||null;
    const to=document.getElementById('dcTo')?.value||null;
    const projectId=document.getElementById('dcProject')?.value||null;
    const status=document.getElementById('dcStatus')?.value||'all';
    global.sendToCSharp?.('REQUEST_HISTORY','ALL',JSON.stringify({from,to,projectId,status}));
  }

  const origRender=global.renderDhList;
  global.renderDhList=function(projs){
    if(_historyRows.length){
      const grouped={};
      _historyRows.forEach(r=>{
        if(!grouped[r.projectId]) grouped[r.projectId]={id:r.projectId,name:r.name,addedAt:r.addedAt,fileType:r.fileType,pipes:[]};
        grouped[r.projectId].pipes.push({id:r.pipe.id,size:r.pipe.size,material:r.pipe.material,status:r.pipe.status,totalLength:r.pipe.totalLength||0});
      });
      const list=Object.values(grouped);
      global._lastDhProjs=list;
      return origRender?.call(this,list);
    }
    return origRender?.apply(this,arguments);
  };

  global.dcSearch=function(){ requestHistory(); };
  global.dcGetFiltered=function(){ return global._lastDhProjs || []; };
  global.initDataHistory=function(){ requestHistory(); };

  const origMsg=global.onCSharpMessage;
  global.onCSharpMessage=function(msg){
    if(msg?.type==='historyData' && Array.isArray(msg.data)){
      _historyRows=msg.data;
      global.renderDhList?.(global._lastDhProjs||[]);
      return;
    }
    origMsg?.apply(this,arguments);
    if(msg?.type==='authResult') window.dispatchEvent(new CustomEvent('csharp-auth',{detail:msg}));
  };
})(window);
