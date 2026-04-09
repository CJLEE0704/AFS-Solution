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
    const q=ProjectStore.ensureQueue(pid);
    if(!q.length) return null;
    const pipeId=q[0];
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
      // 기존 라인 스케줄러를 우선 사용해 연속 생산성을 유지
      const ret=origRun?.apply(this,arguments);
      // 보조 동기화: 선택된 프로젝트가 있으면 현재 선택 배관만 맞춘다(큐 소진 X)
      const p=nextPipeFromActiveProject();
      if(p && !global.processRunning){
        global.opSel=p;
      }
      return ret;
    };

    const origUpdate=global.updateViewersForPipe;
    global.updateViewersForPipe=function(idx){
      // idx 기반으로 뷰어를 갱신해야 다수 배관 연속 표시가 유지된다.
      const pipe=(global.PIPE_DATA?.[idx % (global.PIPE_DATA?.length||1)]) || global.opSel;
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
