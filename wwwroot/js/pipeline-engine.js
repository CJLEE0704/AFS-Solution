(function(global){
  const HOLD_REASON={
    UPSTREAM_NOT_READY:'UPSTREAM_NOT_READY',
    DOWNSTREAM_BUSY:'DOWNSTREAM_BUSY',
    MACHINE_FAULT:'MACHINE_FAULT',
    NO_QUEUE:'NO_QUEUE'
  };

  function chooseBendingTarget(cands){
    const scored=(cands||[]).map(c=>({
      id:c,
      eta:(global.stageSlots?.[c]?.eta)||0,
      qlen:(global.stageSlots?.[c]?.queueLen)||0,
      fault:(global.machSt?.[c]?.alarm)?1:0,
      compatible:(global.stageSlots?.[c]?.compatible===false)?1:0
    }));
    scored.sort((a,b)=>(a.fault-b.fault)||(a.compatible-b.compatible)||(a.eta-b.eta)||(a.qlen-b.qlen));
    return scored[0]?.id||cands?.[0]||1;
  }

  function nextPipeFromActiveProject(){
    const pid=ProjectStore.state.activeProjectId || global.activeProjectId;
    if(!pid) return null;
    const pipeId=ProjectStore.shiftPipe(pid);
    if(!pipeId) return null;
    return ProjectStore.findPipe(pipeId)?.pipe || null;
  }

  function syncPipeSelection(pipe){
    if(!pipe) return;
    if(typeof global.selOpPipe==='function') global.selOpPipe(pipe);
    if(typeof global.selWorkerPipe==='function') global.selWorkerPipe(pipe.id);
  }

  function bind(){
    const origRun=global.runPipelineTick;
    global.runPipelineTick=function(){
      const p=nextPipeFromActiveProject();
      if(p){
        global.opSel=p;
        syncPipeSelection(p);
      }
      return origRun?.apply(this,arguments);
    };

    const origUpdate=global.updateViewersForPipe;
    global.updateViewersForPipe=function(idx){
      const pipe=global.opSel || (global.PIPE_DATA?.[idx]);
      if(pipe) syncPipeSelection(pipe);
      return origUpdate?.apply(this,arguments);
    };

    const origSelOp=global.selOpPipe;
    global.selOpPipe=function(pipe){
      if(pipe?.id) ProjectStore.setPipeStatus(pipe.id,pipe.status||'미완료');
      return origSelOp?.apply(this,arguments);
    };

    const origSelWorker=global.selWorkerPipe;
    global.selWorkerPipe=function(pipeId){
      const found=ProjectStore.findPipe(pipeId);
      if(found) global.workerSel=found.pipe;
      return origSelWorker?.apply(this,arguments);
    };

    global.PipelineEngine={HOLD_REASON,chooseBendingTarget,nextPipeFromActiveProject};
  }

  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',bind,{once:true});
  else bind();
})(window);
